import type { CapacitorConfig } from '@capacitor/cli'

/// The native shell. The web assets are bundled into the app rather than loaded from the
/// server (`server.url`): that keeps the app usable with no signal, and it is also what
/// stops App Review from reading the whole thing as a browser bookmark, when the time comes.
///
/// A consequence worth knowing: changing React code no longer reaches the phone by pushing
/// to `main`. Until live updates are wired in, a frontend change needs `npm run build`,
/// `npx cap sync ios`, and a run from Xcode.
const config: CapacitorConfig = {
  appId: 'app.finance.bogdan',
  appName: 'finance',
  webDir: 'dist',
  ios: {
    // The app is dark-first, so the strip behind the status bar should be too — the default
    // white one flashes on every launch.
    backgroundColor: '#0a0a0a',
    contentInset: 'always',
  },
  plugins: {
    Preferences: {
      // Shared with the widget extension: the widget is a separate process and can only
      // read what is deliberately put in the App Group.
      group: 'group.app.finance.bogdan',
    },
  },
}

export default config
