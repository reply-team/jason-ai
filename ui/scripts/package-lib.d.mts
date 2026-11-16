import type { Plugin } from 'vite';

export function manifestOf(version: string, apiVersion: string): string;
export function buildManifest(options: { version: string; apiVersion: string }): Plugin;
export function buildNotices(): Plugin;
export function packageUi(dist: string, out: string, options: { version: string; apiVersion: string }): Promise<string>;
export function buildSealedPage(): Plugin;
export const sealedPage: { from: string; to: string };
export const sealedCarries: { name: string; version: string; licence: string; file: string }[];
