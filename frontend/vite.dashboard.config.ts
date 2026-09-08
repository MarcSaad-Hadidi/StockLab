import { mergeConfig } from 'vite'
import config from './vite.config.ts'

// Keep the legacy output directory, with every destination linked by Dashboard.
export default mergeConfig(config, {
  build: {
    outDir: 'dist-dashboard',
    emptyOutDir: true,
  },
})
