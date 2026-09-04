export type ProfileData = {
  name: string
  email: string
  createdAt: string
  initialCapital: string
  phone: string
  country: string
  timezone: string
}

export const initialProfile: ProfileData = {
  name: 'Alex Johnson',
  email: 'alex.johnson@example.com',
  createdAt: 'May 20, 2024',
  initialCapital: '$100,000',
  phone: '+1 (555) 123-4567',
  country: 'United States',
  timezone: '(UTC-4) Eastern Time (US & Canada)',
}
