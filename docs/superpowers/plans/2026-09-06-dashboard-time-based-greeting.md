# Dashboard Time-Based Greeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Dashboard welcome text use the browser’s local time while preserving the existing welcome row layout, typography, spacing, and header structure.

**Architecture:** Keep the time-period decision in a pure TypeScript module that accepts an optional `Date` for deterministic boundary tests and an optional existing user name. `DashboardPage` initializes greeting state from `new Date()` and uses one self-rescheduling timeout to update at the next period boundary; the timeout is cleared on unmount. The existing `welcome-row`, `eyebrow`, `h1`, and decorative greeting span remain in place, with only their greeting content made dynamic.

**Tech Stack:** React 19, TypeScript, Vite, Node’s built-in test runner.

**Spec:** User request in the current conversation; there is no separate repository specification file.

## Global Constraints

- Use the browser’s local time from `new Date()`.
- `05:00–11:59` displays `Good morning`; `12:00–17:59` displays `Good afternoon`; `18:00–04:59` displays `Good evening`.
- Use the existing Dashboard name `Ghaith` only when it is already available; no API call, backend change, or new user-data source.
- With no usable name, display the period greeting without a name.
- Preserve the current welcome row’s position, typography, size, spacing, classes, and header structure; do not edit `DashboardPage.css`.
- Update at period boundaries with a single scheduled timeout, not a per-second timer, and clean it up on unmount.
- Test all three periods and the exact `05:00`, `12:00`, and `18:00` boundaries, plus the overnight `04:59`/`05:00` transition.

---

### Task 1: Add deterministic greeting and next-boundary utilities

**Files:**
- Create: `frontend/src/dashboard/dashboardGreeting.ts`
- Test: `frontend/tests/dashboardGreeting.test.ts`

**Interfaces:**
- Produces `getTimeBasedGreeting(date?: Date, name?: string): string`, returning the greeting text without the decorative UI icon.
- Produces `getMillisecondsUntilNextGreetingChange(date: Date): number`, returning the positive local-time delay until the next period boundary.

- [ ] **Step 1: Write the failing tests**

Create Node tests that import the two named utilities and assert:

```ts
test('returns the named greeting for morning, afternoon, and evening', () => {
  assert.equal(getTimeBasedGreeting(localDate(5, 0), 'Ghaith'), 'Good morning, Ghaith')
  assert.equal(getTimeBasedGreeting(localDate(12, 0), 'Ghaith'), 'Good afternoon, Ghaith')
  assert.equal(getTimeBasedGreeting(localDate(18, 0), 'Ghaith'), 'Good evening, Ghaith')
})

test('uses evening overnight and falls back when the name is unavailable', () => {
  assert.equal(getTimeBasedGreeting(localDate(4, 59)), 'Good evening')
  assert.equal(getTimeBasedGreeting(localDate(10, 30), '  '), 'Good morning')
})

test('changes periods exactly at 05:00, 12:00, and 18:00', () => {
  assert.equal(getTimeBasedGreeting(localDate(4, 59)), 'Good evening')
  assert.equal(getTimeBasedGreeting(localDate(5, 0)), 'Good morning')
  assert.equal(getTimeBasedGreeting(localDate(11, 59)), 'Good morning')
  assert.equal(getTimeBasedGreeting(localDate(12, 0)), 'Good afternoon')
  assert.equal(getTimeBasedGreeting(localDate(17, 59)), 'Good afternoon')
  assert.equal(getTimeBasedGreeting(localDate(18, 0)), 'Good evening')
})

test('schedules the next update at the next period boundary', () => {
  assert.equal(getMillisecondsUntilNextGreetingChange(localDate(11, 59, 59, 999)), 1)
  assert.equal(getMillisecondsUntilNextGreetingChange(localDate(17, 59, 59, 999)), 1)
  assert.equal(getMillisecondsUntilNextGreetingChange(localDate(23, 0)), 6 * 60 * 60 * 1000)
})
```

Define `localDate(hour, minute, second = 0, millisecond = 0)` in the test file with `new Date(2026, 5, 1, hour, minute, second, millisecond)` so assertions use local browser-style date construction.

- [ ] **Step 2: Run the focused tests and verify they fail for the missing utilities**

Run from `frontend`:

```powershell
node --experimental-strip-types --test tests/dashboardGreeting.test.ts
```

Expected: the test run fails because `src/dashboard/dashboardGreeting.ts` does not yet exist or does not export the requested utilities.

- [ ] **Step 3: Write the minimal implementation**

Implement the two exports in `frontend/src/dashboard/dashboardGreeting.ts`:

```ts
export function getTimeBasedGreeting(date = new Date(), name?: string): string {
  const hour = date.getHours()
  const period = hour < 5 ? 'Good evening' : hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening'
  const normalizedName = name?.trim()
  return normalizedName ? `${period}, ${normalizedName}` : period
}

export function getMillisecondsUntilNextGreetingChange(date: Date): number {
  const nextChange = new Date(date)
  const hour = date.getHours()

  if (hour < 5) nextChange.setHours(5, 0, 0, 0)
  else if (hour < 12) nextChange.setHours(12, 0, 0, 0)
  else if (hour < 18) nextChange.setHours(18, 0, 0, 0)
  else {
    nextChange.setDate(nextChange.getDate() + 1)
    nextChange.setHours(5, 0, 0, 0)
  }

  return nextChange.getTime() - date.getTime()
}
```

- [ ] **Step 4: Run the focused tests and verify they pass**

Run:

```powershell
node --experimental-strip-types --test tests/dashboardGreeting.test.ts
```

Expected: all greeting and boundary tests pass with zero failures.

- [ ] **Step 5: Commit the utility and tests**

```powershell
git add frontend/src/dashboard/dashboardGreeting.ts frontend/tests/dashboardGreeting.test.ts
git commit -m "test: cover dashboard greeting periods"
```

### Task 2: Wire the greeting into DashboardPage

**Files:**
- Modify: `frontend/src/dashboard/DashboardPage.tsx:1, 265-296`
- Do not modify: `frontend/src/dashboard/DashboardPage.css`

**Interfaces:**
- Consumes `getTimeBasedGreeting` and `getMillisecondsUntilNextGreetingChange` from `./dashboardGreeting`.
- Uses the existing Dashboard display name `Ghaith`; no new API or backend dependency.

- [ ] **Step 1: Add the state/effect wiring after the existing state declarations**

Import `useEffect`, `getTimeBasedGreeting`, and `getMillisecondsUntilNextGreetingChange`. Add a local existing-name constant and initialize the greeting from the local browser time:

```ts
const dashboardUserName = 'Ghaith'

const [greeting, setGreeting] = useState(() => getTimeBasedGreeting(new Date(), dashboardUserName))

useEffect(() => {
  let timeoutId: number

  const scheduleGreetingUpdate = () => {
    const now = new Date()
    setGreeting(getTimeBasedGreeting(now, dashboardUserName))
    timeoutId = window.setTimeout(scheduleGreetingUpdate, getMillisecondsUntilNextGreetingChange(now))
  }

  timeoutId = window.setTimeout(() => {
    scheduleGreetingUpdate()
  }, getMillisecondsUntilNextGreetingChange(new Date()))

  return () => window.clearTimeout(timeoutId)
}, [])
```

Keep all existing `welcome-row` classes and element placement. Render the state in the existing `h1` and retain the current decorative span position, changing only its glyph to the requested waving-hand content:

```tsx
<h1>{greeting} <span>👋</span></h1>
```

- [ ] **Step 2: Run lint and the production build**

Run from `frontend`:

```powershell
npm run lint
npm run build
```

Expected: both commands exit with code 0 and report no TypeScript or ESLint errors.

- [ ] **Step 3: Run the full existing test set plus greeting tests**

Run from `frontend`:

```powershell
node --experimental-strip-types --test tests/*.test.ts
```

Expected: every repository test passes, including the three period scenarios and exact boundary assertions.

- [ ] **Step 4: Verify the final diff is limited to the requested behavior**

Run:

```powershell
git diff --stat HEAD~2..HEAD
git diff -- frontend/src/dashboard/DashboardPage.tsx frontend/src/dashboard/dashboardGreeting.ts frontend/tests/dashboardGreeting.test.ts frontend/src/dashboard/DashboardPage.css
```

Confirm that only Dashboard greeting behavior and its tests changed, with no API/backend files and no CSS changes.

- [ ] **Step 5: Commit the Dashboard integration**

```powershell
git add frontend/src/dashboard/DashboardPage.tsx
git commit -m "feat: add time-based dashboard greeting"
```

