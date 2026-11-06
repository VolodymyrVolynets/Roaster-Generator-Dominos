import assert from 'node:assert/strict'
import test from 'node:test'
import { checklistSummary, fieldsChanged, fieldsFor, fitWithin, groupSignFields, missingSignFields, stateFor, toDocumentListQuery, toFormFields, toSubmitRequest } from './documents.js'

test('fitWithin never scales up', () => {
  assert.deepEqual(fitWithin(800, 600), { width: 800, height: 600 })
})

test('fitWithin limits the long edge of narrow images', () => {
  assert.deepEqual(fitWithin(6000, 1000), { width: 2000, height: 333 })
})

test('fitWithin limits the total pixels of phone photos', () => {
  const { width, height } = fitWithin(4032, 3024)

  assert.ok(width * height <= 2_200_000 * 1.001)
  assert.equal(Math.round((width / height) * 100), 133)
})

test('fieldsFor shows only what a document type has', () => {
  const printed = { code: 'passport', hasReference: true, expiryRule: 'Printed' }
  assert.deepEqual(fieldsFor(printed), ['holderName', 'referenceNumber', 'issuingCountry', 'expiryDate'])
  assert.deepEqual(fieldsFor({ code: 'irp', hasReference: true, expiryRule: 'Printed' }),
    ['holderName', 'referenceNumber', 'issuingCountry', 'stamp', 'expiryDate'])
  assert.deepEqual(fieldsFor({ code: 'nct', hasReference: false, expiryRule: 'Printed' }),
    ['holderName', 'vehicleRegistration', 'expiryDate'])
  assert.deepEqual(fieldsFor({ code: 'pp', hasReference: false, expiryRule: 'PenaltyPoints' }),
    ['holderName', 'signingDate', 'penaltyPoints'])
  assert.deepEqual(fieldsFor({ code: 'spf', hasReference: false, expiryRule: 'None' }), ['holderName'])
})

test('toSubmitRequest turns blanks into null and points into a number', () => {
  const request = toSubmitRequest(
    { ...toFormFields(), holderName: ' JOHN ', penaltyPoints: '0', expiryDate: '2030-01-01' }, 'type', '  ')

  assert.equal(request.holderName, 'JOHN')
  assert.equal(request.penaltyPoints, 0)
  assert.equal(request.expiryDate, '2030-01-01')
  assert.equal(request.signingDate, null)
  assert.equal(request.note, null)
  assert.equal(request.documentTypeId, 'type')
})

test('toSubmitRequest keeps blank points as null', () => {
  assert.equal(toSubmitRequest(toFormFields(), 'type', '').penaltyPoints, null)
})

test('toFormFields replaces null so inputs stay controlled', () => {
  assert.equal(toFormFields({ holderName: null, penaltyPoints: 0 }).holderName, '')
  assert.equal(toFormFields({ penaltyPoints: 0 }).penaltyPoints, 0)
})

test('fieldsChanged ignores surrounding whitespace', () => {
  const original = toFormFields({ referenceNumber: 'AB1', penaltyPoints: 2 })

  assert.equal(fieldsChanged(original, { ...original, referenceNumber: ' AB1 ' }), false)
  assert.equal(fieldsChanged(original, { ...original, penaltyPoints: '2' }), false)
  assert.equal(fieldsChanged(original, { ...original, referenceNumber: 'AB2' }), true)
})

test('checklistSummary counts only required documents that need action', () => {
  assert.deepEqual(checklistSummary([
    { isRequired: true, state: 'Valid' },
    { isRequired: true, state: 'Expired' },
    { isRequired: true, state: 'NotUploaded' },
    { isRequired: true, state: 'WrongVehicle' },
    { isRequired: true, state: 'NeedsVehicleUpdate' },
    { isRequired: true, state: 'DependsOnPassport' },
    { isRequired: false, state: 'Expired' },
  ]), { required: 6, outstanding: 4 })
})

test('toDocumentListQuery leaves out empty filters', () => {
  assert.equal(toDocumentListQuery({ search: '  ', documentTypeId: '', state: '', includeInactive: false }), '')
  assert.equal(
    toDocumentListQuery({ search: ' Anna B ', documentTypeId: 't', state: 'Expired', includeInactive: true }),
    '?search=Anna+B&documentTypeId=t&state=Expired&includeInactive=true',
  )
})

test('stateFor describes unsigned forms as not signed', () => {
  assert.equal(stateFor({ kind: 'Sign', state: 'NotUploaded' }).label, 'Not signed yet')
  assert.equal(stateFor({ kind: 'Upload', state: 'NotUploaded' }).label, 'Not uploaded yet')
  assert.equal(stateFor({ kind: 'Sign', state: 'Expired' }).label, 'Expired')
})

test('missingSignFields finds empty required text and unticked boxes', () => {
  const fields = [
    { name: 'a', required: true }, { name: 'b', required: false },
    { name: 'c', required: true, isCheckbox: true }, { name: 'd', required: true },
  ]

  assert.deepEqual(missingSignFields(fields, { a: '  ', c: 'false', d: 'x' }).map((field) => field.name), ['a', 'c'])
})

test('groupSignFields pairs yes and no into one question', () => {
  const groups = groupSignFields([
    { name: 'name', isCheckbox: false, label: 'Name' },
    { name: 'licence_yes', isCheckbox: true, label: 'Current licence: Yes' },
    { name: 'licence_no', isCheckbox: true, label: 'Current licence: No' },
    { name: 'avail_mon', isCheckbox: true, label: 'Monday' },
  ])

  assert.deepEqual(groups.map((group) => group.kind), ['field', 'question', 'field'])
  assert.equal(groups[1].label, 'Current licence')
  assert.equal(groups[1].no.name, 'licence_no')
})
