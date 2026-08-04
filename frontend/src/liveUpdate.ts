import { CapacitorUpdater } from '@capgo/capacitor-updater'
import { isNative } from './native'

/// A new build of the front end reaches the phone without Xcode.
///
/// Capacitor packs the web assets inside the app, so without this every React change would mean
/// a rebuild and a reinstall — killing the "lived with it → it grates → fixed the same day"
/// loop this project runs on. Xcode is now only needed when native code changes.
///
/// An update is **not** applied on the fly: it waits for the next launch. Swapping the page out
/// from under someone who is entering an expense loses what they were entering.

/// Marking the build as healthy has to be done explicitly. If it never happens, an update that
/// does not start is rolled back and the app returns to the last working version. Without this
/// one broken deploy would brick the phone until a cable was involved.
export async function markRunningVersionHealthy(): Promise<void> {
  if (!isNative()) return

  try {
    await CapacitorUpdater.notifyAppReady()
  } catch (e) {
    // Not fatal: at worst the update rolls back, which is exactly the behaviour wanted when
    // something has gone wrong.
    console.warn('notifyAppReady failed', e)
  }
}
