/**
 * Returns the value when it is the NOM authority's OIDC authorize request, so a
 * visitor who left another app's sign-in to register can be sent back to it.
 */
export function authorizeReturnUrl(value: string | null | undefined): string | null {
  return value === '/connect/authorize' || value?.startsWith('/connect/authorize?') ? value : null;
}
