# React hooks audit: ten corrected snippets

These standalone fragments are the Week 3/5 hooks-audit artifact. Domain helpers and presentational
components are intentionally placeholders so each file stays focused on one lifecycle defect. Every
snippet includes the correction and a nearby explanation.

| # | Snippet | Failure mode | Correction |
|---|---|---|---|
| 1 | Notification badge | Missing `userId` dependency and an obsolete request race | Depend on `userId`; ignore completion after cleanup |
| 2 | Window width tracker | `window` read during server render and leaked listener | Read after mount; remove the same callback |
| 3 | Polling counter | Interval closes over the initial count and survives unmount | Functional state update; `clearInterval` cleanup |
| 4 | Toast | Obsolete timeout invokes `onDismiss` | Clear timeout; restart for `message`/callback changes |
| 5 | Filter panel | Fresh object identity defeats stable memoization | Construct options inside `useMemo`; depend on `statuses` |
| 6 | Live feed | Previous channel subscription remains active | Return the subscription's unsubscribe cleanup |
| 7 | Notification list | Callback captures the first active tab | Include `activeTab` in `useCallback` dependencies |
| 8 | Notification detail | Slower previous request overwrites current detail | Ignore completion after dependency change/unmount |
| 9 | Unread counter | Batched updates reuse captured state and can go negative | One clamped functional update in a stable callback |
| 10 | Notification sound | Audio instance continues after disable/unmount | Create only when enabled; pause and rewind in cleanup |

The equivalent class lifecycle mapping is: effect setup performs mount/update synchronization, while
the returned function performs both pre-next-effect cleanup and unmount cleanup. Dependencies describe
every reactive value read by the effect; they are not a scheduling preference.
