(function () {
  "use strict";

  function currentApiClient() {
    return (
      window.ApiClient ||
      (window.connectionManager && window.connectionManager.currentApiClient &&
        window.connectionManager.currentApiClient()) ||
      null
    );
  }

  function callIfFunction(target, member) {
    if (!target) return null;
    try {
      var value = target[member];
      if (typeof value === "function") {
        return value.call(target);
      }
      return value;
    } catch (e) {
      return null;
    }
  }

  function normalizeServerAddress(value) {
    if (!value || typeof value !== "string") {
      return "";
    }

    try {
      var parsed = new URL(value, window.location.origin);
      var pathname = (parsed.pathname || "").replace(/\/$/, "");
      return parsed.origin + pathname;
    } catch (e) {
      return value.replace(/\/$/, "");
    }
  }

  function readServerAddress(api) {
    var direct = callIfFunction(api, "serverAddress");
    if (typeof direct === "string" && direct) {
      return normalizeServerAddress(direct);
    }

    var info = callIfFunction(api, "serverInfo");
    if (info && typeof info === "object") {
      var fromInfo =
        info.ManualAddress ||
        info.manualAddress ||
        info.Address ||
        info.address ||
        info.Url ||
        info.url;
      if (typeof fromInfo === "string" && fromInfo) {
        return normalizeServerAddress(fromInfo);
      }
    }

    return "";
  }

  function readAccessToken(api) {
    var token = callIfFunction(api, "accessToken");
    if (typeof token === "string" && token) {
      return token;
    }

    var info = callIfFunction(api, "serverInfo");
    if (info && typeof info === "object") {
      var fromInfo =
        info.AccessToken || info.accessToken || info.Token || info.token;
      if (typeof fromInfo === "string" && fromInfo) {
        return fromInfo;
      }
    }

    return "";
  }

  function readUserId(api) {
    var direct =
      callIfFunction(api, "getCurrentUserId") || callIfFunction(api, "userId");
    if (typeof direct === "string" && direct) {
      return direct;
    }

    var currentUser = callIfFunction(api, "currentUser");
    if (currentUser && typeof currentUser === "object") {
      var fromCurrent =
        currentUser.Id || currentUser.id || currentUser.UserId || currentUser.userId;
      if (typeof fromCurrent === "string" && fromCurrent) {
        return fromCurrent;
      }
    }

    var info = callIfFunction(api, "serverInfo");
    if (info && typeof info === "object") {
      var fromInfo =
        info.UserId || info.userId || info.CurrentUserId || info.currentUserId;
      if (typeof fromInfo === "string" && fromInfo) {
        return fromInfo;
      }
    }

    return "";
  }

  function persistBootstrapCredentials() {
    var api = currentApiClient();
    if (!api) return;

    var payload = {
      serverAddress: readServerAddress(api),
      accessToken: readAccessToken(api),
      userId: readUserId(api),
      source: "jellyfin_loader",
      timestamp: Date.now(),
    };

    if (!payload.serverAddress || !payload.accessToken || !payload.userId) {
      return;
    }

    var raw;
    try {
      raw = JSON.stringify(payload);
    } catch (e) {
      return;
    }

    try {
      window.sessionStorage.setItem("moonfin_bootstrap_credentials", raw);
    } catch (e) {}

    try {
      window.localStorage.setItem("moonfin_bootstrap_credentials", raw);
    } catch (e) {}
  }

  function resolveMoonfinBase() {
    try {
      var api = currentApiClient();
      if (api && typeof api.serverAddress === "function") {
        var server = api.serverAddress() || "";
        if (server) {
          var parsed = new URL(server, window.location.origin);
          var prefix = (parsed.pathname || "").replace(/\/$/, "");
          return prefix + "/Moonfin";
        }
      }
    } catch (e) {}

    var path = window.location.pathname || "";
    var webIdx = path.toLowerCase().lastIndexOf("/web/");
    if (webIdx >= 0) {
      return path.substring(0, webIdx) + "/Moonfin";
    }

    return "/Moonfin";
  }

  function injectHeaderButton() {
    if (document.querySelector(".headerMoonfinButton")) return;

    var syncBtn = document.querySelector(".headerSyncButton");
    var headerRight = syncBtn
      ? syncBtn.parentNode
      : document.querySelector(".headerRight");
    if (!headerRight) return;

    var moonfinBase = resolveMoonfinBase();
    var btn = document.createElement("button");
    btn.type = "button";
    btn.className = "headerButton headerButtonRight headerMoonfinButton";
    btn.title = "Open Moonfin";
    btn.innerHTML =
      '<img src="' +
      moonfinBase +
      '/Assets/icon.png" style="width:24px;height:24px;border-radius:4px;vertical-align:middle" alt="Moonfin">';
    btn.addEventListener("click", function () {
      persistBootstrapCredentials();
      window.location.href = moonfinBase + "/Web/";
    });

    if (syncBtn) {
      headerRight.insertBefore(btn, syncBtn);
    } else {
      headerRight.insertBefore(btn, headerRight.firstChild);
    }
  }

  if (document.readyState === "complete") {
    setTimeout(injectHeaderButton, 200);
  } else {
    window.addEventListener("load", function () {
      setTimeout(injectHeaderButton, 200);
    });
  }

  var lastHeader = null;
  setInterval(function () {
    var header = document.querySelector(".headerRight");
    if (header && header !== lastHeader) {
      lastHeader = header;
      injectHeaderButton();
    }
  }, 1000);
})();
