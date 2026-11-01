/* global DotNet -- provided by the Blazor runtime */
// The OpenTelemetry SDK in the browser batches what it records before sending it. When the page is hidden or unloaded (the tab
// is closed or navigated away, the user switches tab, ...) whatever is still queued would be lost, and the traces it belongs to
// would reach the backend without their root: the server's spans arrive, the browser's (their parents) never do. The exporter
// sends every 250ms, so little is ever queued; this flushes what is, as a best effort (a browser may tear the page down mid-send).
window.grotsTelemetry = {
    registerFlush() {
        const flush = () => {
            try {
                // Synchronous on purpose: the page may be gone before an asynchronous call completes.
                DotNet.invokeMethod('GrotsPizzeria.Client', 'FlushTelemetry');
            } catch {
                // The runtime is not available (yet, or any more): nothing to flush.
            }
        };

        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'hidden') {
                flush();
            }
        });
        // beforeunload fires while the page is still fully alive, so it is the most reliable of the three; pagehide and
        // visibilitychange cover what it does not (mobile browsers, switching tabs).
        window.addEventListener('beforeunload', flush);
        window.addEventListener('pagehide', flush);
    },
};
