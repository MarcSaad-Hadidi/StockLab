import { getAuthSession } from '../auth/authStorage'
import { pages } from './routes'

window.location.replace(getAuthSession() ? pages.dashboard.path : pages.login.path)
