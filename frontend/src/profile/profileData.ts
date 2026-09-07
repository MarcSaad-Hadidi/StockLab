export type ProfileData = {
  name: string
  email: string
  createdAt: string
  initialCapital: number
  phone: string
  country: string
  timezone: string
}

export const initialProfile: ProfileData = {
  name: 'Alex Johnson',
  email: 'alex.johnson@example.com',
  createdAt: '2024-05-20',
  initialCapital: 100000,
  phone: '+1 (555) 123-4567',
  country: 'us',
  timezone: 'americaEasternDaylight',
}
