// Records the reader's IANA time zone in a cookie, so the server can render timestamps in it from the
// very first render.
//
// A separate file rather than an inline script in the hosting view, and this is load-bearing: a CMS
// back office served under a `script-src 'self'` policy — with no nonce or hash for inline code, which
// is the ordinary case — never runs an inline script at all. The cookie would then never be set, and
// the entire viewer-time-zone design would silently reduce to permanent UTC on every page view for
// ever. Labelled UTC, so never *wrong*, but permanently dead.
//
// A cookie rather than JS interop because the value has to exist *before* rendering: a component
// reading the zone over the circuit would resolve it on the prerender pass and lose it the instant the
// circuit took over. The cost is that the first ever page view renders in UTC; every one after is
// correct at prerender with no flicker, and nothing here reloads the page to avoid that one occurrence.
(function () {
    try {
        var zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        if (!zone) {
            return;
        }

        var cookie = 'sji-timezone=' + encodeURIComponent(zone) + '; path=/; max-age=31536000; SameSite=Lax';

        // Secure only over HTTPS: setting it unconditionally would make the cookie unsettable on a
        // plain-HTTP development host, which is where this is most often first tried out.
        if (window.location.protocol === 'https:') {
            cookie += '; Secure';
        }

        document.cookie = cookie;
    } catch (e) {
        // No Intl support, or cookies blocked. The server keeps rendering UTC, which the page says.
    }
})();
