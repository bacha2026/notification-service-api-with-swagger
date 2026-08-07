function NotificationSound({ enabled }) {
  const audioRef = useRef(null);

  // Missing cleanup: replaced audio could keep playing; pausing and clearing the instance releases it when the effect ends.
  useEffect(() => {
    const audio = new Audio('/ping.mp3');
    audioRef.current = audio;
    if (enabled) {
      audio.play();
    }

    return () => {
      audio.pause();
      audioRef.current = null;
    };
  }, [enabled]);

  return null;
}
