import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  use: { baseURL: 'http://127.0.0.1:5150', browserName: 'chromium' },
  webServer: {
    command: 'npm run dev',
    url: 'http://127.0.0.1:5150',
    reuseExistingServer: !process.env.CI,
  },
})
