// ZeroSports: live countdowns + graceful team-logo fallbacks.

function zeroPad(n) { return String(n).padStart(2, "0"); }

function renderCountdown(el) {
    var start = new Date(el.getAttribute("data-start")).getTime();
    var diff = start - Date.now();

    if (diff <= 0) {
        el.innerHTML = '<span class="badge-live">LIVE NOW!</span>';
        return;
    }

    var total = Math.floor(diff / 1000);
    var h = Math.floor(total / 3600);
    var m = Math.floor((total % 3600) / 60);
    var s = total % 60;

    el.innerHTML = 'Starts in: <strong>' + zeroPad(h) + ':' + zeroPad(m) + ':' + zeroPad(s) + '</strong>';
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
