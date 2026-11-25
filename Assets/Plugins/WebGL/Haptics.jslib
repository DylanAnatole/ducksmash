mergeInto(LibraryManager.library, {
  DS_Vibrate: function (ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) {}
  },
  // touch devices that Unity's isMobilePlatform misses (iPadOS Safari reports itself as a Mac)
  DS_IsTouchDevice: function () {
    try {
      var ua = navigator.userAgent || "";
      if (/iP(hone|ad|od)|Android/i.test(ua)) return 1;
      if (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1) return 1;
    } catch (e) {}
    return 0;
  }
});
