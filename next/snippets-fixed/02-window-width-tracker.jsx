function WindowWidthTracker() {
  const [width, setWidth] = useState(null);

  // Missing cleanup + browser-only render access: read window after mount, then
  // remove the exact listener on unmount to prevent duplicate updates.
  useEffect(() => {
    function handleResize() {
      setWidth(window.innerWidth);
    }

    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  return <p>Window width: {width ?? 'measuring…'}</p>;
}
