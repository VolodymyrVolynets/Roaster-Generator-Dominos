import { useState } from 'react'
import { WeekSelector } from '../../components/SelectionControls'
import { AppShell } from '../../components/AppShell'
import { AbsentPanel } from '../absence/AbsentPanel'
import { fetchJson } from '../../api/client'
import { ErrorPopup } from '../../components/ErrorPopup'
import { ScheduleEditor } from '../availability/ScheduleEditor'
import { HolidayPanel } from '../leave/HolidayPanels'
import { SickLeavePanel } from '../leave/SickLeavePanels'
import { PersonalRosterPanel } from '../roster/PersonalRosterPanel'
import { maxWeekOffset, minWeekOffset } from '../../lib/format'

export function DriverWorkspace(props) {
  return (
    <EmployeeWorkspace
      {...props}
      variant="driver"
      title="Driver workspace"
      lead="Set the hours you are available to work for the selected week."
    />
  )
}

export function InStoreWorkspace(props) {
  return (
    <EmployeeWorkspace
      {...props}
      variant="instore"
      title="In-store workspace"
      lead="Set the hours you are available to work for the selected week."
    />
  )
}

export function EmployeeWorkspace({
  variant,
  title,
  lead,
  children,
  authState,
  employee,
  errorPopup,
  employeesState,
  scheduleState,
  schedule,
  saveState,
  weekOffset,
  setWeekOffset,
  saveSchedule,
  updateDay,
  resetDay,
  setErrorPopup,
  logout,
  realtimeVersions = {},
}) {
  const [activeTab, setActiveTab] = useState('availability')
  const isDriver = variant === 'driver'
  const roleLabel = isDriver ? 'Driver' : variant === 'instore' ? 'In-store' : 'Employee'
  const navGroups = [{
    label: 'My work',
    items: [
      { id: 'availability', label: 'Availability', icon: 'calendar' },
      isDriver && { id: 'roster', label: 'My roster', icon: 'grid' },
      { id: 'holiday', label: 'Holiday', icon: 'sun' },
      { id: 'sick-leave', label: 'Sick leave', icon: 'cross' },
      isDriver && { id: 'absent', label: 'Absent', icon: 'absent' },
    ].filter(Boolean),
  }]
  const showsWeek = activeTab === 'availability' || activeTab === 'roster'

  return (
    <>
      <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
      <AppShell
        variant={`employee ${variant}-workspace`}
        consoleLabel={title}
        roleLabel={roleLabel}
        userName={authState.user.employeeName || authState.user.username}
        navGroups={navGroups}
        activeId={activeTab}
        onNavigate={setActiveTab}
        onSignOut={logout}
        toolbar={showsWeek && (
          <WeekSelector
            weekOffset={weekOffset}
            onChange={setWeekOffset}
            disabled={scheduleState.status === 'loading' || saveState.status === 'saving'}
            minimumWeekOffset={minWeekOffset}
            maximumWeekOffset={maxWeekOffset}
          />
        )}
      >
        {activeTab === 'holiday' ? (
          <HolidayPanel setErrorPopup={setErrorPopup} realtimeKey={realtimeVersions.holiday} />
        ) : activeTab === 'sick-leave' ? (
          <SickLeavePanel setErrorPopup={setErrorPopup} realtimeKey={realtimeVersions.sickLeave} />
        ) : activeTab === 'absent' && isDriver ? (
          <AbsentPanel fetchJson={fetchJson} realtimeKey={realtimeVersions.absent + realtimeVersions.employees + realtimeVersions.rosterDrivers} />
        ) : activeTab === 'roster' && isDriver ? (
          <PersonalRosterPanel weekOffset={weekOffset} setErrorPopup={setErrorPopup} realtimeKey={realtimeVersions.roster} />
        ) : (
          <section className="employee-workspace" aria-label="Your availability">
            {children}

            {employeesState.status === 'error' && (
              <p className="message error-message">{employeesState.message}</p>
            )}

            {employeesState.status === 'success' && !employee && (
              <p className="message info-message">Your employee profile is not available.</p>
            )}

            {scheduleState.status === 'loading' && (
              <p className="message info-message" role="status">Loading availability…</p>
            )}

            {scheduleState.status === 'error' && (
              <p className="message error-message">{scheduleState.message}</p>
            )}

            <ScheduleEditor
              schedule={schedule}
              saveState={saveState}
              saveSchedule={saveSchedule}
              updateDay={updateDay}
              resetDay={resetDay}
            />
          </section>
        )}
      </AppShell>
    </>
  )
}
