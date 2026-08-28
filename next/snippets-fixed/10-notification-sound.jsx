function NotificationSound({ enabled }) {
  // Missing cleanup: create audio only while enabled, then pause and rewind that
  // exact instance when enabled changes or the component unmounts.
  useEffect(() => {
    if (!enabled) return;

    const audio = new Audio('/ping.mp3');
    void audio.play().catch(() => {
      // Autoplay policy may require a prior user interaction.
    });

    return () => {
      audio.pause();
      audio.currentTime = 0;
    };
  }, [enabled]);

  return null;
}
