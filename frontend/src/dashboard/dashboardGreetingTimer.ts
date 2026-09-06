import { getMillisecondsUntilNextGreetingChange, getTimeBasedGreeting } from './dashboardGreeting.ts'

type GreetingTimerOptions = {
  now?: () => Date
  setTimeoutFn?: typeof window.setTimeout
  clearTimeoutFn?: typeof window.clearTimeout
}

export function startDashboardGreetingTimer(
  setGreeting: (greeting: string) => void,
  userName: string,
  options: GreetingTimerOptions = {},
) {
  let timeoutId: number
  const { clearTimeoutFn = window.clearTimeout, now = () => new Date(), setTimeoutFn = window.setTimeout } = options

  const scheduleGreetingUpdate = () => {
    const currentDate = now()
    setGreeting(getTimeBasedGreeting(currentDate, userName))
    timeoutId = setTimeoutFn(scheduleGreetingUpdate, getMillisecondsUntilNextGreetingChange(currentDate))
  }

  scheduleGreetingUpdate()
  return () => clearTimeoutFn(timeoutId)
}
