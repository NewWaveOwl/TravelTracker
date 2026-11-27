// Each request owns its close listener, including when a newer navigation supersedes it.
const pendingTripConfirmations = new WeakMap();

window.confirmTripChanges = (dialog, message) => {
    pendingTripConfirmations.get(dialog)?.(false);
    dialog.querySelector("p").textContent = message;
    dialog.returnValue = "stay";
    if (!dialog.open) dialog.showModal();
    dialog.querySelector("[autofocus]").focus();
    return new Promise(resolve => {
        function finish(discard) {
            dialog.removeEventListener("close", onClose);
            pendingTripConfirmations.delete(dialog);
            resolve(discard);
        }
        function onClose() {
            if (!dialog.open) finish(dialog.returnValue === "discard");
        }
        pendingTripConfirmations.set(dialog, finish);
        dialog.addEventListener("close", onClose);
    });
};
