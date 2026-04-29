import { test, expect } from '@playwright/test';

// Phase 1 placeholder smoke test. Real E2E coverage lands in tasks.md
// T149 (onboard-client run with hand-offs) and T150 (dangerous-action
// confirmation loop).

test('homepage renders the platform header', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Agent Platform' })).toBeVisible();
});
