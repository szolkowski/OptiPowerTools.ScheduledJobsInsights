// Scroll helpers for the execution log viewer.
//
// The log is Virtualize-backed, so only the visible slice exists in the DOM and there is no element
// to anchor to at either end. Setting scrollTop directly is the reliable way to jump: Virtualize
// reacts to the scroll event and renders whatever slice lands in view.
//
// Loaded as a JS module via IJSRuntime.InvokeAsync<IJSObjectReference>("import", ...) so it is
// fetched only when the detail view is actually opened, and disposed with the component.

export function scrollToTop(element) {
    element?.scrollTo({ top: 0, behavior: 'auto' });
}

export function scrollToEnd(element) {
    if (element) {
        element.scrollTo({ top: element.scrollHeight, behavior: 'auto' });
    }
}
