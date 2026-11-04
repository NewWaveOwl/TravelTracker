/**
 * Maps known provider failures to safe messages without displaying raw diagnostics.
 * @param error SDK failure, potentially containing sensitive provider details.
 * @return Static recovery text safe to render in the sign-in feedback panel.
 */
export function loginFailure(error: unknown): string {
  const value = error && typeof error === 'object' ? error as Record<string, unknown> : {};
  const description = typeof value.error_description === 'string' ? value.error_description : '';
  if (description.includes('is not authorized to access resource server')) return 'Sign-in is not available for this application yet. Ask the administrator to authorize its access, then try again.';
  if (description.includes('Service not found')) return 'The sign-in service is not fully configured. Ask the administrator to check the API settings, then try again.';
  if (value.error === 'login_required') return 'Your sign-in session has ended. Sign in again to continue.';
  if (value.error === 'consent_required') return 'Permission is needed to continue. Sign in again and review the access request.';
  return 'Sign-in was canceled or could not be completed. You can try again.';
}
