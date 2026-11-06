export const documentCategories = [
  { id: 'CarOwn', label: 'Driver: own car' },
  { id: 'CarCompany', label: 'Driver: company car' },
  { id: 'MopedOwn', label: 'Driver: own moped' },
  { id: 'MopedCompany', label: 'Driver: company moped' },
  { id: 'EBikeOwn', label: 'Driver: own e-bike' },
  { id: 'EBikeCompany', label: 'Driver: company e-bike' },
  { id: 'InStore', label: 'In-store' },
  { id: 'Manager', label: 'Manager' },
]

export const requirementConditions = [
  { id: 'Always', label: 'Always' },
  { id: 'NonEuPassport', label: 'Non-EU only' },
  { id: 'Stamp2', label: 'Stamp 2 only' },
]

export const documentKinds = [
  { id: 'Upload', label: 'Uploaded by the employee' },
  { id: 'Sign', label: 'Signed in the app' },
]

export const fieldSources = [
  { id: 'Ask', label: 'Ask the employee' },
  { id: 'FullName', label: 'Full name' },
  { id: 'FirstName', label: 'First name' },
  { id: 'LastName', label: 'Last name' },
  { id: 'EmployeeNumber', label: 'Employee number' },
  { id: 'PhoneNumber', label: 'Phone number' },
  { id: 'PayrollNumber', label: 'Payroll number' },
  { id: 'VehicleRegistration', label: 'Vehicle registration' },
  { id: 'PenaltyPoints', label: 'Penalty points (ask)' },
  { id: 'Today', label: "Today's date" },
  { id: 'StoreId', label: 'Store ID' },
  { id: 'StoreName', label: 'Store name' },
  { id: 'FranchiseeName', label: 'Franchisee name' },
  { id: 'AlwaysTicked', label: 'Always ticked' },
  { id: 'TickedForMoped', label: 'Ticked for moped drivers' },
  { id: 'Signature', label: 'Employee signature' },
  { id: 'ManagerName', label: 'Manager name' },
  { id: 'ManagerSignature', label: 'Manager signature' },
  { id: 'ManagerDate', label: 'Manager signing date' },
  { id: 'Ignore', label: 'Leave blank' },
]

export const expiryRules = [
  { id: 'Printed', label: 'Printed expiry date' },
  { id: 'None', label: 'No expiry' },
  { id: 'PenaltyPoints', label: 'Calculated from penalty points' },
]

export const expiryRuleLabel = (rule) => expiryRules.find((item) => item.id === rule)?.label ?? rule

/** Turns the API lists into { category: { documentTypeId: condition } }. */
export function toDrafts(lists) {
  return Object.fromEntries(lists.map(({ category, items }) => [
    category,
    Object.fromEntries(items.map(({ documentTypeId, condition }) => [documentTypeId, condition])),
  ]))
}

export function setRequirement(draft, documentTypeId, condition) {
  const next = { ...draft }
  if (condition) next[documentTypeId] = condition
  else delete next[documentTypeId]
  return next
}

export function draftsEqual(first = {}, second = {}) {
  const firstKeys = Object.keys(first)
  return firstKeys.length === Object.keys(second).length && firstKeys.every((key) => first[key] === second[key])
}

/** Keeps catalogue order, so the saved list matches what the employee will see. */
export function toListRequest(draft, types) {
  return {
    items: types
      .filter((type) => draft[type.id])
      .map((type) => ({ documentTypeId: type.id, condition: draft[type.id] })),
  }
}

/** Inactive types stay visible only while a list still includes them, so they can be removed. */
export const visibleTypes = (types, draft) => types.filter((type) => type.isActive || draft[type.id])
