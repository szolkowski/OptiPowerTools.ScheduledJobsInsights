// Browser-side helpers for the execution detail view.
//
// Loaded as a JS module via IJSRuntime.InvokeAsync<IJSObjectReference>("import", ...) so it is
// fetched only when the detail view is actually opened, and disposed with the component.

// The log is Virtualize-backed, so only the visible slice exists in the DOM and there is no element
// to anchor to at either end. Setting scrollTop directly is the reliable way to jump: Virtualize
// reacts to the scroll event and renders whatever slice lands in view.

export function scrollToTop(element) {
    element?.scrollTo({ top: 0, behavior: 'auto' });
}

export function scrollToEnd(element) {
    if (element) {
        element.scrollTo({ top: element.scrollHeight, behavior: 'auto' });
    }
}

// Copies a result summary to the clipboard, reporting success so the caller can label the button
// honestly. navigator.clipboard only exists in a secure context, so this returns false rather than
// throwing when the CMS is served over plain HTTP on a non-localhost host.
export async function copyText(text) {
    if (!navigator.clipboard) {
        return false;
    }

    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        // Permission denied, or the document lost focus mid-write.
        return false;
    }
}
