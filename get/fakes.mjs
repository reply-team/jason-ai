// Stand-ins for what Cloudflare hands the handler: the D1 binding, the context, the outbound fetch and the clock, and
// a signer for the tokens Access puts on a request. Not a test file itself, so the runner does not pick it up by name.

import { readFileSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { handle } from './src/index.mjs';

/** A page that begins as the guide does: the first heading of docs/INSTALL.md, read from the tree, and a line. */
export const GUIDE = [readFileSync(new URL('../docs/INSTALL.md', import.meta.url), 'utf8').split('\n')[0], '', 'A line of the guide.', ''].join('\n');

/** The address the tests send requests to. Written once, so no address of another shape stands in a test file. */
export const BASE = 'https://get.jasonharness.ai';

/** The team and the application audience the tests' Access is configured with. Made up. */
export const TEAM = 'example-team';
export const AUD = 'aud-made-up-for-tests';

/** A D1 binding that records every statement it runs, and answers a read with the rows it was given. */
export function fakeDb({ rows = [], failing = false } = {}) {
  const runs = [];
  const reads = [];
  return {
    runs,
    reads,
    prepare(sql) {
      if (failing) throw new Error('the store is down');
      return {
        bind(...args) {
          return {
            async run() {
              runs.push({ sql, args });
              return { success: true };
            },
            async all() {
              reads.push({ sql, args });
              return { results: rows };
            },
          };
        },
      };
    },
  };
}

/**
 * A D1 binding over a real SQLite database in memory, made from schema.sql, so a statement is run as written. D1 numbers
 * its parameters (?1, ?2) and the statement goes to node:sqlite untouched. Handed the values positionally, node:sqlite
 * (22.17) refuses a numbered parameter ("column index out of range"), so they are bound by name, '?1' for ?1, which it takes.
 */
export function sqliteDb() {
  const database = new DatabaseSync(':memory:');
  database.exec(readFileSync(new URL('./schema.sql', import.meta.url), 'utf8'));
  const reads = [];
  return {
    reads,
    database,
    prepare(sql) {
      return {
        bind(...args) {
          const statement = database.prepare(sql);
          const named = Object.fromEntries(args.map((value, index) => ['?' + (index + 1), value]));
          return {
            async run() {
              statement.run(named);
              return { success: true };
            },
            async all() {
              reads.push({ sql, args });
              return { results: statement.all(named).map((row) => ({ ...row })) };
            },
          };
        },
      };
    },
  };
}

/** The execution context: what the handler leaves for after its answer, kept so a test can wait for it. */
export function fakeCtx() {
  const pending = [];
  return {
    pending,
    waitUntil(promise) {
      pending.push(promise);
    },
    async settled() {
      return Promise.allSettled(pending);
    },
  };
}

/** An outbound fetch that answers by address from a table, and records every address it was asked for. */
export function fakeFetch(table) {
  const asked = [];
  const fetch = async (input, init) => {
    const address = typeof input === 'string' ? input : input.url;
    asked.push({ address, init });
    const answer = table[address];
    if (answer === undefined) throw new Error('no such address in this test');
    if (answer instanceof Error) throw answer;
    return typeof answer === 'function' ? answer() : answer.clone();
  };
  fetch.asked = asked;
  return fetch;
}

/** Sends one request through the handler and waits for what it left running. */
export async function send(path, { method = 'GET', headers = {}, env = {}, fetch = fakeFetch({}), now = new Date('2026-11-12T10:00:00Z') } = {}) {
  const ctx = fakeCtx();
  const response = await handle(new Request(BASE + path, { method, headers }), env, ctx, { fetch, now: () => now });
  await ctx.settled();
  return { response, ctx };
}

const encoder = new TextEncoder();

function base64url(bytes) {
  return Buffer.from(bytes).toString('base64url');
}

/** An RSA key pair as Access signs with, and its public half as the certs address lists it. */
export async function accessKey(kid) {
  const pair = await crypto.subtle.generateKey(
    { name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' },
    true,
    ['sign', 'verify'],
  );
  const jwk = await crypto.subtle.exportKey('jwk', pair.publicKey);
  return { kid, privateKey: pair.privateKey, jwk: { kid, kty: jwk.kty, alg: 'RS256', use: 'sig', e: jwk.e, n: jwk.n } };
}

/** A token as Access writes one, signed by the key given, with the claims given over sound defaults. */
export async function accessToken(key, claims = {}, header = {}) {
  const now = Math.floor(new Date('2026-11-12T10:00:00Z').getTime() / 1000);
  const head = { alg: 'RS256', kid: key.kid, typ: 'JWT', ...header };
  const body = {
    aud: [AUD],
    email: 'someone@example.com',
    exp: now + 600,
    iat: now - 60,
    nbf: now - 60,
    iss: `https://${TEAM}.cloudflareaccess.com`,
    sub: 'made-up',
    ...claims,
  };
  const data = `${base64url(encoder.encode(JSON.stringify(head)))}.${base64url(encoder.encode(JSON.stringify(body)))}`;
  const signature = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key.privateKey, encoder.encode(data));
  return `${data}.${base64url(new Uint8Array(signature))}`;
}

/** The certs address of the tests' team, answering the keys given. */
export function certs(...keys) {
  return {
    [`https://${TEAM}.cloudflareaccess.com/cdn-cgi/access/certs`]: () =>
      new Response(JSON.stringify({ keys: keys.map((key) => key.jwk) }), { headers: { 'content-type': 'application/json' } }),
  };
}
