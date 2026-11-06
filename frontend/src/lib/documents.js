export const maxDocumentFiles = 4
const maxLongEdge = 2000
const maxPixels = 2_200_000
const vehicleCodes = new Set(['nct', 'motor-tax', 'insurance', 'obu'])
const countryCodes = new Set(['passport', 'driving-licence', 'irp'])

export const documentFilterStates = [
  'WaitingForReview', 'WaitingForManager', 'WrongVehicle', 'NeedsVehicleUpdate', 'Expired', 'ExpiringSoon', 'NotUploaded', 'Valid',
]

export const fieldLabels = {
  holderName: 'Name on document',
  referenceNumber: 'Reference number',
  issuingCountry: 'Issuing country code',
  stamp: 'Stamp',
  vehicleRegistration: 'Vehicle registration',
  expiryDate: 'Expiry date',
  signingDate: 'Signing date',
  penaltyPoints: 'Penalty points',
}

export const stateDetails = {
  Valid: { label: 'Valid', tone: 'good' },
  ExpiringSoon: { label: 'Expiring soon', tone: 'warning' },
  WaitingForReview: { label: 'Waiting for review', tone: 'neutral' },
  WaitingForManager: { label: 'Waiting for manager', tone: 'neutral' },
  NeedsNewSignature: { label: 'Please sign again', tone: 'warning' },
  NeedsVehicleUpdate: { label: 'Needs updating for new car', tone: 'warning' },
  WrongVehicle: { label: 'Wrong vehicle', tone: 'danger' },
  NeedsAdminDecision: { label: 'An admin will confirm', tone: 'neutral' },
  DependsOnPassport: { label: 'Depends on passport', tone: 'neutral' },
  NotUploaded: { label: 'Not uploaded yet', tone: 'warning' },
  Expired: { label: 'Expired', tone: 'danger' },
}

const needsAction = new Set(['NotUploaded', 'Expired', 'WrongVehicle', 'NeedsVehicleUpdate', 'NeedsNewSignature'])

export function checklistSummary(items) {
  const required = items.filter((item) => item.isRequired)
  const outstanding = required.filter((item) => needsAction.has(item.state)).length
  return { required: required.length, outstanding }
}

/** Large photos are scaled down to keep fine print readable at a lower cost. Small images stay as they are. */
export function fitWithin(width, height) {
  const scale = Math.min(1, maxLongEdge / Math.max(width, height), Math.sqrt(maxPixels / (width * height)))
  return { width: Math.round(width * scale), height: Math.round(height * scale) }
}

/** The fields worth showing for a document type, so nobody is asked about things it doesn't have. */
export function fieldsFor(type) {
  if (!type) return ['holderName', 'referenceNumber', 'expiryDate']
  return [
    'holderName',
    type.hasReference && 'referenceNumber',
    countryCodes.has(type.code) && 'issuingCountry',
    type.code === 'irp' && 'stamp',
    vehicleCodes.has(type.code) && 'vehicleRegistration',
    type.expiryRule === 'Printed' && 'expiryDate',
    type.expiryRule === 'PenaltyPoints' && 'signingDate',
    type.expiryRule === 'PenaltyPoints' && 'penaltyPoints',
  ].filter(Boolean)
}

const emptyFields = {
  holderName: '', referenceNumber: '', issuingCountry: '', issueDate: '', expiryDate: '',
  stamp: '', vehicleRegistration: '', signingDate: '', penaltyPoints: '',
}

export function toFormFields(fields = {}) {
  return Object.fromEntries(Object.keys(emptyFields).map((key) => [key, fields[key] ?? '']))
}

/** Empty inputs become null, which the API needs for dates and numbers. */
export function toSubmitRequest(fields, documentTypeId, note) {
  const values = Object.fromEntries(Object.keys(emptyFields).map((key) => {
    const value = typeof fields[key] === 'string' ? fields[key].trim() : fields[key]
    return [key, value === '' || value === undefined ? null : value]
  }))
  return {
    ...values,
    penaltyPoints: values.penaltyPoints === null ? null : Number(values.penaltyPoints),
    documentTypeId,
    note: note.trim() || null,
  }
}

export function fieldsChanged(original, current) {
  return Object.keys(emptyFields).some((key) => String(original[key]).trim() !== String(current[key]).trim())
}

export function toDocumentListQuery({ search, documentTypeId, state, includeInactive }) {
  const query = new URLSearchParams()
  if (search.trim()) query.set('search', search.trim())
  if (documentTypeId) query.set('documentTypeId', documentTypeId)
  if (state) query.set('state', state)
  if (includeInactive) query.set('includeInactive', 'true')
  const text = query.toString()
  return text ? `?${text}` : ''
}

/** Signed forms read differently from uploads while nothing is on file yet. */
export function stateFor(item) {
  if (item.kind === 'Sign' && item.state === 'NotUploaded') return { label: 'Not signed yet', tone: 'warning' }
  return stateDetails[item.state] ?? { label: item.state, tone: 'neutral' }
}

/**
 * Makes long forms easier on a phone: fields named x_yes and x_no become one question, and
 * neighbouring fields labelled "Group: Field" fold into one section.
 */
export function groupSignFields(fields) {
  const byName = new Map(fields.map((field) => [field.name, field]))
  const groups = []
  for (const field of fields) {
    if (field.isCheckbox && field.name.endsWith('_no') && byName.has(`${field.name.slice(0, -3)}_yes`)) continue
    const no = field.isCheckbox && field.name.endsWith('_yes') ? byName.get(`${field.name.slice(0, -4)}_no`) : null
    if (no) {
      groups.push({ kind: 'question', key: field.name, label: field.label.replace(/:\s*Yes$/, ''), yes: field, no })
      continue
    }

    const [title, ...rest] = field.label.split(': ')
    const previous = groups.at(-1)
    if (!field.isCheckbox && rest.length > 0) {
      const part = { ...field, label: rest.join(': ') }
      if (previous?.kind === 'section' && previous.title === title) previous.fields.push(part)
      else groups.push({ kind: 'section', key: field.name, title, fields: [part] })
      continue
    }
    groups.push({ kind: 'field', key: field.name, field })
  }
  return groups
}

export function missingSignFields(fields, values) {
  return fields.filter((field) => field.required &&
    (field.isCheckbox ? values[field.name] !== 'true' : !String(values[field.name] ?? '').trim()))
}

export const auditActionLabels = {
  Uploaded: 'Uploaded',
  Signed: 'Signed',
  ManagerSigned: 'Signed by manager',
  Replaced: 'Replaced',
  Viewed: 'Viewed',
  Downloaded: 'Downloaded',
  Edited: 'Edited',
  Reviewed: 'Reviewed',
  Deleted: 'Deleted',
  Analysed: 'Checked',
}
