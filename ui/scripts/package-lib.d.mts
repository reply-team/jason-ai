import type { Plugin } from 'vite';

export function manifestOf(version: string, apiVersion: string): string;
export function buildManifest(options: { version: string; apiVersion: string }): Plugin;
export function packageUi(dist: string, out: string, options: { version: string; apiVersion: string }): Promise<string>;
