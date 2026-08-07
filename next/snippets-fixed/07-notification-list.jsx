function NotificationList({ notifications, activeTab }) {
  // Missing dependency: dismissals used the tab from the first render; depending on activeTab keeps the callback current.
  const handleDismiss = useCallback((id) => {
    dismissNotification(id, activeTab);
  }, [activeTab]);

  return notifications.map((n) => (
    <NotificationRow key={n.id} notification={n} onDismiss={handleDismiss} />
  ));
}
