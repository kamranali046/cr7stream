// CR7Stream: live countdowns + graceful team-logo fallbacks.

function zeroPad(n) { return String(n).padStart(2, "0"); }

function renderCountdown(el) {
    var start = new Date(el.getAttribute("data-start")).getTime();
    var diff = start - Date.now();

    if (diff <= 0) {
        var badge = document.createElement("span");
        badge.className = "badge-live";
        badge.textContent = "LIVE NOW!";
        el.replaceChildren(badge);
        return;
    }

    var total = Math.floor(diff / 1000);
    var h = Math.floor(total / 3600);
    var m = Math.floor((total % 3600) / 60);
    var s = total % 60;

    el.replaceChildren("Starts in: ", Object.assign(document.createElement("strong"), { textContent: zeroPad(h) + ":" + zeroPad(m) + ":" + zeroPad(s) }));
}

function tickCountdowns() {
    document.querySelectorAll(".countdown[data-start]").forEach(renderCountdown);
}

document.addEventListener("DOMContentLoaded", function () {
    tickCountdowns();
    setInterval(tickCountdowns, 1000);
});

// Replace broken team/league logos with initials so the UI never looks broken.
document.addEventListener("error", function (e) {
    var img = e.target;
    if (!img || img.tagName !== "IMG" || img.dataset.fallback) return;
    img.dataset.fallback = "1";
    var text = (img.getAttribute("alt") || "?").trim().split(/\s+/).map(function (w) { return w[0]; }).join("").slice(0, 3).toUpperCase();
    var span = document.createElement("span");
    span.className = "logo-fallback";
    span.textContent = text;
    span.style.cssText = "width:" + (img.width || 40) + "px;height:" + (img.height || 40) + "px;border-radius:50%;background:#232c3d;color:#fff;display:inline-flex;align-items:center;justify-content:center;font-weight:700;font-size:13px;";
    if (img.parentNode) img.parentNode.replaceChild(span, img);
}, true);
