// The check of the token Cloudflare Access puts on a request it let through, made here as well as at Access.
//
// Cloudflare, https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/validating-json/ :
// "When Cloudflare sends a request to your origin, the request will include an application token as a
// `Cf-Access-Jwt-Assertion` request header."
// "Cloudflare signs the token with a key pair unique to your account. You should validate the token with your public
// key to ensure that the request came from Access and not a malicious third party."
// "The public key for the signing key pair is located at
// `https://<your-team-name>.cloudflareaccess.com/cdn-cgi/access/certs`, where `<your-team-name>` is your Cloudflare One
// team name."
// "By default, Access rotates the signing key every 6 weeks." "Previous keys remain valid for 7 days after rotation to
// allow time for you to make the update."
// "Cloudflare Access assigns a unique AUD tag to each application. The `aud` claim in the token payload specifies which
// application the JWT is valid for."
// The team's name and the application's AUD tag are the deployment's secrets, ACCESS_TEAM and ACCESS_AUD; without
// either, nothing is let through.

const TEAM = /^[a-z0-9-]{1,63}$/i;

/** How long the team's keys are kept, and how soon a token naming a key they lack may have them read again. */
const KEEP_MS = 60 * 60 * 1000;
const REREAD_MS = 5 * 60 * 1000;

let kept = null; // { team, keys: Map(kid -> jwk), readAt }

/** Forgets the keys read, so a test starts from none. */
export function forgetKeys() {
  kept = null;
}

const decoder = new TextDecoder();
const encoder = new TextEncoder();

function fromBase64url(text) {
  if (!/^[A-Za-z0-9_-]*$/.test(text)) throw new Error('not base64url');
  const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

async function readKeys(team, fetch, now) {
  const response = await fetch(`https://${team}.cloudflareaccess.com/cdn-cgi/access/certs`);
  if (response.status !== 200) throw new Error('the team keys could not be read');
  const body = await response.json();
  const keys = new Map();
  for (const jwk of Array.isArray(body.keys) ? body.keys : []) {
    if (jwk && typeof jwk.kid === 'string' && jwk.kty === 'RSA') keys.set(jwk.kid, jwk);
  }
  kept = { team, keys, readAt: now };
  return keys;
}

async function keyFor(team, kid, fetch, now) {
  const fresh = kept && kept.team === team && now - kept.readAt < KEEP_MS;
  if (fresh && kept.keys.has(kid)) return kept.keys.get(kid);
  if (fresh && now - kept.readAt < REREAD_MS) return undefined;
  return (await readKeys(team, fetch, now)).get(kid);
}

/** Whether the request carries a token Access signed for this application, from this team, valid now. */
export async function accessAllows(request, env, { fetch, now }) {
  try {
    const team = env.ACCESS_TEAM;
    const audience = env.ACCESS_AUD;
    if (typeof team !== 'string' || !TEAM.test(team) || typeof audience !== 'string' || audience === '') return false;

    const token = request.headers.get('cf-access-jwt-assertion');
    if (!token) return false;
    const parts = token.split('.');
    if (parts.length !== 3) return false;
    const header = JSON.parse(decoder.decode(fromBase64url(parts[0])));
    const claims = JSON.parse(decoder.decode(fromBase64url(parts[1])));
    if (header.alg !== 'RS256' || typeof header.kid !== 'string') return false;

    const moment = now().getTime();
    const jwk = await keyFor(team, header.kid, fetch, moment);
    if (!jwk) return false;
    const key = await crypto.subtle.importKey('jwk', { kty: jwk.kty, n: jwk.n, e: jwk.e, alg: 'RS256', ext: true }, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']);
    const signed = await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, fromBase64url(parts[2]), encoder.encode(`${parts[0]}.${parts[1]}`));
    if (!signed) return false;

    const seconds = Math.floor(moment / 1000);
    const audiences = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
    if (!audiences.includes(audience)) return false;
    if (claims.iss !== `https://${team}.cloudflareaccess.com`) return false;
    if (typeof claims.exp !== 'number' || claims.exp <= seconds) return false;
    if (claims.nbf !== undefined && (typeof claims.nbf !== 'number' || claims.nbf > seconds + 60)) return false;
    return true;
  } catch {
    return false;
  }
}
