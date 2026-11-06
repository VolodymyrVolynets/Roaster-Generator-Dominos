import assert from 'node:assert/strict'
import test from 'node:test'
import { checklistSummary, fieldsChanged, fieldsFor, fitWithin, mergeFields, toDocumentListQuery, toFormFields, toSubmitRequest } from './documents.js'

test('fitWithin never scales up', () => {
  assert.deepEqual(fitWithin(800, 600), { width: 800, height: 600 })
})

test('fitWithin limits the long edge of narrow images', () => {
  assert.deepEqual(fitWithin(6000, 1000), { width: 2576, height: 429 })
})

test('fitWithin limits the total pixels of phone photos', () => {
  const { width, height } = fitWithin(4032, 3024)

  assert.ok(width * height <= 3_750_000 * 1.001)
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
    { isRequired: true, state: 'DependsOnPassport' },
    { isRequired: false, state: 'Expired' },
  ]), { required: 4, outstanding: 2 })
})

test('mergeFields keeps typed values and takes new readings for the rest', () => {
  const original = toFormFields({ referenceNumber: 'AB1', holderName: 'JOHN' })
  const current = { ...original, referenceNumber: 'AB2' }
  const next = toFormFields({ referenceNumber: 'AB1', holderName: 'JOHN SMITH', expiryDate: '2030-01-01' })

  const merged = mergeFields(original, current, next)

  assert.equal(merged.referenceNumber, 'AB2')
  assert.equal(merged.holderName, 'JOHN SMITH')
  assert.equal(merged.expiryDate, '2030-01-01')
})

test('toDocumentListQuery leaves out empty filters', () => {
  assert.equal(toDocumentListQuery({ search: '  ', documentTypeId: '', state: '', includeInactive: false }), '')
  assert.equal(
    toDocumentListQuery({ search: ' Anna B ', documentTypeId: 't', state: 'Expired', includeInactive: true }),
    '?search=Anna+B&documentTypeId=t&state=Expired&includeInactive=true',
  )
})
