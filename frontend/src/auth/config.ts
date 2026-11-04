export type AuthConfig = { domain: string; clientId: string; audience: string; scope: string };

/**
 * Reads public deployment settings without accepting client secrets.
 * @param env Public Vite environment values.
 * @return Complete Auth0 settings, or null until configured.
 */
export function readAuthConfig(env: Record<string, string | undefined>): AuthConfig | null {
  const domain = env.VITE_AUTH0_DOMAIN?.trim();
  const clientId = env.VITE_AUTH0_CLIENT_ID?.trim();
  const audience = env.VITE_AUTH0_AUDIENCE?.trim();
  if (!domain || !clientId || !audience || !/^[a-z0-9.-]+$/i.test(domain) || !domain.includes('.') || domain.includes('..')) return null;
  return { domain, clientId, audience, scope: env.VITE_AUTH0_SCOPE?.trim() || 'openid profile email catalogue.read' };
}

export const authConfig = readAuthConfig(import.meta.env);
