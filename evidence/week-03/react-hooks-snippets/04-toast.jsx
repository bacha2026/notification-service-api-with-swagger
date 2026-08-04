function Toast({ message, onDismiss }) {
  // Missing cleanup: old or unmounted toasts could still dismiss; clearing the timeout cancels obsolete callbacks.
  useEffect(() => {
    const id = setTimeout(() => {
      onDismiss();
    }, 3000);
    return () => clearTimeout(id);
  }, [message, onDismiss]);

  return <div className="toast">{message}</div>;
}
