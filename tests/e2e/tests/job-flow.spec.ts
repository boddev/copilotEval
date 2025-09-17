import { test, expect } from '@playwright/test';

test.describe('Job Submission and Status Flow', () => {
  test('should submit a job and track status', async ({ page }) => {
    // Navigate to the application
    await page.goto('/');

    // Check if the page loads correctly
    await expect(page).toHaveTitle(/Copilot Evaluation Tool/);

    // Look for job submission form
    await expect(page.getByText('Submit New Job')).toBeVisible();

    // Fill out the job submission form
    await page.getByLabel('Job Name').fill('E2E Test Job');
    await page.getByLabel('Description').fill('End-to-end test job submission');
    await page.getByLabel('Data Source').fill('test-data.csv');
    await page.getByLabel('Prompt Template').fill('Test prompt: {context}');

    // Submit the job
    await page.getByRole('button', { name: 'Submit Job' }).click();

    // Check for success message or job ID
    // Note: This would need to be adjusted based on actual UI behavior
    await expect(page.getByText(/job.*submitted/i)).toBeVisible({ timeout: 10000 });
  });

  test('should display jobs list', async ({ page }) => {
    await page.goto('/');

    // Check if jobs list is accessible
    // This test assumes there's a way to view jobs list
    await expect(page.getByText(/jobs/i)).toBeVisible();
  });

  test('should handle form validation', async ({ page }) => {
    await page.goto('/');

    // Try to submit empty form
    await page.getByRole('button', { name: 'Submit Job' }).click();

    // Check for validation errors
    await expect(page.getByText(/job name is required/i)).toBeVisible();
    await expect(page.getByText(/data source is required/i)).toBeVisible();
    await expect(page.getByText(/prompt template is required/i)).toBeVisible();
  });

  test('should be responsive', async ({ page }) => {
    // Test mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/');

    // Check if the page is still usable on mobile
    await expect(page.getByText('Submit New Job')).toBeVisible();
    await expect(page.getByLabel('Job Name')).toBeVisible();
  });
});