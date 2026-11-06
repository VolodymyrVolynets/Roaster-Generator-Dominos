export const maxDocumentFiles = 4
const maxLongEdge = 2576
const maxPixels = 3_750_000
const vehicleCodes = new Set(['nct', 'motor-tax', 'insurance', 'obu'])
const countryCodes = new Set(['passport', 'driving-licence', 'irp'])

export const documentFilterStates = [
  'WaitingForReview', 'Expired', 'ExpiringSoon', 'NotUploaded', 'Valid',
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
  NeedsAdminDecision: { label: 'An admin will confirm', tone: 'neutral' },
  DependsOnPassport: { label: 'Depends on passport', tone: 'neutral' },
  NotUploaded: { label: 'Not uploaded yet', tone: 'warning' },
  Expired: { label: 'Expired', tone: 'danger' },
}

const needsAction = new Set(['NotUploaded', 'Expired'])

export function checklistSummary(items) {
  const required = items.filter((item) => item.isRequired)
  const outstanding = required.filter((item) => needsAction.has(item.state)).length
  return { required: required.length, outstanding }
}

/** Scales down to what Claude reads at full resolution, keeping the aspect ratio. Never scales up. */
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

/** A new analysis, after adding a page, must not undo what the person already typed. */
export function mergeFields(previousOriginal, current, next) {
  return Object.fromEntries(Object.keys(emptyFields).map((key) => [
    key,
    String(previousOriginal[key]) === String(current[key]) ? next[key] : current[key],
  ]))
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

export const auditActionLabels = {
  Uploaded: 'Uploaded',
  Replaced: 'Replaced',
  Viewed: 'Viewed',
  Downloaded: 'Downloaded',
  Edited: 'Edited',
  Reviewed: 'Reviewed',
  Deleted: 'Deleted',
  Analysed: 'Checked',
}
