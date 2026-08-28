# Angular OnPush demonstration

This small Angular 22 application is the Week 5 frontend audit deliverable. Both the root component and
the notification-list child use `ChangeDetectionStrategy.OnPush`.

The UI makes three change-detection rules observable:

1. **Same-reference mutation is not an OnPush input change.** The first button pushes into the existing
   array. The parent can display the mutated length when another signal marks it, while the child keeps
   its old rows because the input reference did not change.
2. **Immutable replacement crosses the input boundary.** The second button creates a new array. Angular
   marks the child because its input reference changed, and the child renders the complete collection.
3. **Signals integrate with OnPush.** Updating the counter signal refreshes its consumer without calling
   `ChangeDetectorRef.detectChanges()` or `markForCheck()` manually.

Run it locally:

```bash
npm install
npm start
```

Verify the behavioral tests and production build:

```bash
npm test -- --watch=false
npm run build
```

Build and run the optional, non-root Nginx container used by the Week 5 Compose profile:

```bash
docker build -t angular-onpush-demo .
docker run --rm -p 4200:8080 angular-onpush-demo
```

The runtime serves SPA routes through `index.html`, gives fingerprinted assets immutable cache headers,
keeps the HTML shell uncached, and exposes `GET /healthz` for the image health check.
