function Toast({ message, onDismiss }) {
  // Missing cleanup: old or unmounted toasts could still dismiss. Clearing the
  // timeout cancels obsolete callbacks; message intentionally resets the timer.
  useEffect(() => {
    const id = setTimeout(() => {
      onDismiss();
    }, 3000);
    return () => clearTimeout(id);
  }, [message, onDismiss]);

  return <div className="toast">{message}</div>;
}
