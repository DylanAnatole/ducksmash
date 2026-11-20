mergeInto(LibraryManager.library, {
  DS_Vibrate: function (ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) {}
  }
});
