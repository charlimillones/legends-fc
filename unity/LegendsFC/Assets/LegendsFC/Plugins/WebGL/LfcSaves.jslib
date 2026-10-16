// The FC web version (Oct 15): saves are files in the browser's storage (IndexedDB, mounted by Unity at persistentDataPath).
// App.FlushSaves() calls this after the game saves, so the files reach IndexedDB before the tab can be closed.
mergeInto(LibraryManager.library, {
  LfcSyncFs: function () {
    try {
      FS.syncfs(false, function (err) { if (err) console.warn("[LFC] couldn't flush the saves", err); });
    } catch (e) {
      console.warn("[LFC] couldn't flush the saves", e);
    }
  }
});
