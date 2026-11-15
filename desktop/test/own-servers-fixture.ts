// The planted files of the own-servers tests: made-up names beside made-up secrets, none of a real person's.

/** Made-up secrets, each a value a server's entry holds; none may come back. */
export const plantedSecrets = [
    'sk-planted-env-0123456789abcdef',
    'Bearer planted-header-token-7f7f7f',
    '--api-key=planted-arg-secret-42',
    'https://planted-user:planted-pass@mcp.example.invalid/sse?token=planted-url-token',
    'planted-oauth-client-secret-99',
    'npx-planted-command',
] as const;

export const claudeWithSecrets = JSON.stringify({
    numStartups: 3,
    oauthAccount: { emailAddress: 'someone@example.invalid', accountUuid: 'planted-account-uuid' },
    projects: { '/made/up/folder': { mcpServers: { 'project-local-server': { command: 'npx-planted-command' } } } },
    mcpServers: {
        alpha: { type: 'stdio', command: 'npx-planted-command', args: ['--api-key=planted-arg-secret-42'], env: { API_KEY: 'sk-planted-env-0123456789abcdef' } },
        beta: { type: 'http', url: 'https://planted-user:planted-pass@mcp.example.invalid/sse?token=planted-url-token', headers: { Authorization: 'Bearer planted-header-token-7f7f7f' } },
        gamma: { type: 'sse', url: 'https://mcp.example.invalid/', oauth: { clientId: 'x', clientSecret: 'planted-oauth-client-secret-99' } },
    },
});

export const codexWithSecrets = [
    'model = "gpt-made-up"',
    '',
    '[mcp_servers.alpha]',
    'command = "npx-planted-command"',
    'args = ["--api-key=planted-arg-secret-42"]',
    '',
    '[mcp_servers.alpha.env]',
    'API_KEY = "sk-planted-env-0123456789abcdef"',
    '',
    '[mcp_servers.beta]',
    'url = "https://planted-user:planted-pass@mcp.example.invalid/sse?token=planted-url-token"',
    'http_headers = { Authorization = "Bearer planted-header-token-7f7f7f" }',
    '',
    '[mcp_servers.gamma]',
    'url = "https://mcp.example.invalid/"',
    'bearer_token_env_var = "PLANTED_VARIABLE"',
    "client_secret = 'planted-oauth-client-secret-99'",
    '',
].join('\n');
