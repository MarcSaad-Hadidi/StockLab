export type GreetingPeriod = 'morning' | 'afternoon' | 'evening'

export function getGreetingPeriod(date = new Date()): GreetingPeriod {
  const hour = date.getHours()
  return hour < 5 ? 'evening' : hour < 12 ? 'morning' : hour < 18 ? 'afternoon' : 'evening'
}

export function getTimeBasedGreeting(date = new Date(), name?: string): string {
  const period = `Good ${getGreetingPeriod(date)}`
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
