import { useEffect, useState } from 'react'
import { Icon } from './Icon'
import { useTheme } from '../hooks/useTheme'
import { usePresence } from '../hooks/usePresence'

const themeLabels = { system: 'Match system', light: 'Light', dark: 'Dark' }
const themeIcons = { system: 'system', light: 'sun', dark: 'moon' }

function initials(name) {
  return String(name || '?')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('')
}

function NavList({ groups, activeId, onSelect }) {
  return groups.map((group) => (
    <div className="shell-nav-group" key={group.label}>
      <span className="shell-nav-label">{group.label}</span>
      <ul>
        {group.items.map((item) => (
          <li key={item.id}>
            <button
              type="button"
              className={item.id === activeId ? 'shell-nav-item active' : 'shell-nav-item'}
              aria-current={item.id === activeId ? 'page' : undefined}
              onClick={() => onSelect(item.id)}
            >
              <Icon name={item.icon} />
              <span>{item.label}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  ))
}

export function AppShell({
  consoleLabel,
  roleLabel,
  userName,
  navGroups,
  activeId,
  onNavigate,
  toolbar,
  onSignOut,
  variant = 'admin',
  children,
}) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const drawer = usePresence(drawerOpen, 200)
  const { theme, cycleTheme } = useTheme()
  const items = navGroups.flatMap((group) => group.items)
  const activeItem = items.find((item) => item.id === activeId)
  const useTabBar = items.length <= 5

  useEffect(() => {
    if (!drawerOpen) return undefined
    const closeOnEscape = (event) => {
      if (event.key === 'Escape') setDrawerOpen(false)
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [drawerOpen])

  function select(id) {
    setDrawerOpen(false)
    onNavigate(id)
    window.scrollTo({ top: 0 })
  }

  const sidebarBody = (
    <>
      <div className="shell-brand">
        <span className="shell-brand-mark" aria-hidden="true">RG</span>
        <span className="shell-brand-copy">
          <strong>Roaster Generator</strong>
          <span>{consoleLabel}</span>
        </span>
      </div>

      <nav className="shell-nav" aria-label={`${consoleLabel} sections`}>
        <NavList groups={navGroups} activeId={activeId} onSelect={select} />
      </nav>

      <div className="shell-account">
        <div className="shell-account-person">
          <span className="shell-avatar" aria-hidden="true">{initials(userName)}</span>
          <span className="shell-account-copy">
            <strong>{userName}</strong>
            <span>{roleLabel}</span>
          </span>
        </div>
        <div className="shell-account-actions">
          <button
            type="button"
            className="shell-icon-button"
            onClick={cycleTheme}
            aria-label={`Appearance: ${themeLabels[theme]}. Change appearance`}
            title={`Appearance: ${themeLabels[theme]}`}
          >
            <Icon name={themeIcons[theme]} size={18} />
          </button>
          <button type="button" className="shell-icon-button" onClick={onSignOut} aria-label="Sign out" title="Sign out">
            <Icon name="signOut" size={18} />
          </button>
        </div>
      </div>
    </>
  )

  return (
    <div className={`shell shell-${variant}${useTabBar ? ' shell-has-tabbar' : ''}`}>
      <a className="skip-link" href="#main-content">Skip to content</a>

      <aside className="shell-sidebar" aria-label="Application">
        {sidebarBody}
      </aside>

      {drawer.mounted && (
        <div
          className={`shell-drawer-backdrop${drawer.closing ? ' is-closing' : ''}`}
          onClick={() => setDrawerOpen(false)}
        >
          <aside
            className="shell-drawer"
            role="dialog"
            aria-modal="true"
            aria-label="Navigation"
            onClick={(event) => event.stopPropagation()}
          >
            <button type="button" className="shell-icon-button shell-drawer-close" onClick={() => setDrawerOpen(false)} aria-label="Close navigation">
              <Icon name="close" size={18} />
            </button>
            {sidebarBody}
          </aside>
        </div>
      )}

      <div className="shell-main">
        <header className="shell-topbar">
          <button type="button" className="shell-icon-button shell-menu-button" onClick={() => setDrawerOpen(true)} aria-label="Open navigation and account">
            <Icon name="menu" />
          </button>
          <div className="shell-topbar-title">
            <span className="shell-topbar-context">{consoleLabel}</span>
            <strong>{activeItem?.label}</strong>
          </div>
          <div className="shell-topbar-tools">{toolbar}</div>
        </header>

        <main id="main-content" className="shell-content" tabIndex={-1}>
          {children}
        </main>
      </div>

      {useTabBar && (
        <nav className="shell-tabbar" aria-label={`${consoleLabel} sections`}>
          {items.map((item) => (
            <button
              key={item.id}
              type="button"
              className={item.id === activeId ? 'shell-tab active' : 'shell-tab'}
              aria-current={item.id === activeId ? 'page' : undefined}
              onClick={() => select(item.id)}
            >
              <Icon name={item.icon} size={22} />
              <span>{item.label}</span>
            </button>
          ))}
        </nav>
      )}
    </div>
  )
}
