(() => {
  "use strict";

  const STORAGE_KEY = "polar-stream.preferences.v1";
  const defaults = Object.freeze({ streamName: null, lastDevice: null });

  function load() {
    try {
      const stored = JSON.parse(window.localStorage.getItem(STORAGE_KEY) || "null");
      const streamName = typeof stored?.streamName === "string" && stored.streamName
        ? stored.streamName
        : null;
      const lastDevice = typeof stored?.lastDevice?.id === "string"
        && typeof stored?.lastDevice?.name === "string"
        ? { id: stored.lastDevice.id, name: stored.lastDevice.name }
        : null;
      return { streamName, lastDevice };
    } catch (_error) {
      return { ...defaults };
    }
  }

  function write(preferences) {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
    } catch (_error) {
      // The app remains fully usable if WebView storage is disabled.
    }
    return preferences;
  }

  function saveStreamName(streamName) {
    return write({ ...load(), streamName });
  }

  function saveLastDevice(device) {
    return write({ ...load(), lastDevice: { id: device.id, name: device.name } });
  }

  window.PolarPreferences = Object.freeze({
    STORAGE_KEY,
    load,
    saveStreamName,
    saveLastDevice,
  });
})();
