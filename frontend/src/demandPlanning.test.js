import test from 'node:test'
import assert from 'node:assert/strict'
import { calculateDemandLabour, calculateDemandSummary, recalculateDemandPlan, recalculateDemandValue } from './demandPlanning.js'

const settings = { deliveriesPerDriverHour: 2.7 }

test('driver productivity rounds up without floating-point extra staffing', () => {
  assert.deepEqual(recalculateDemandValue({ isOpen: true, deliveries: 8.1 }, settings), {
    isOpen: true, deliveries: 8.1, demand: 3,
  })
  const quiet = recalculateDemandValue({ isOpen: true, deliveries: 0 }, settings)
  assert.equal(quiet.demand, 0)
  assert.equal(recalculateDemandValue({ deliveries: 2.1 }, { ...settings, deliveriesPerDriverHour: 0.3 }).demand, 7)
})

test('missing deliveries stay unknown and closed hours do not create staffing', () => {
  const missing = recalculateDemandValue({ isOpen: true, deliveries: null }, settings)
  assert.equal(missing.demand, null)
  const closed = recalculateDemandValue({ isOpen: false, deliveries: 10 }, settings)
  assert.equal(closed.demand, null)
})

test('editing deliveries updates driver staffing without mutating source data', () => {
  const value = { isOpen: true, deliveries: 9, demand: 2 }
  const edited = recalculateDemandValue(value, settings, ['demand'])
  assert.equal(edited.demand, 4)
  assert.equal(value.demand, 2)
})

test('productivity changes recalculate all row previews without mutating source data', () => {
  const original = { ...settings, rows: [{ hour: 12, values: [{ position: 0, isOpen: true, deliveries: 12, demand: 5 }] }] }
  const preview = recalculateDemandPlan({ ...original, deliveriesPerDriverHour: 6 })
  assert.equal(preview.rows[0].values[0].demand, 2)
  assert.equal(original.rows[0].values[0].demand, 5)
})

test('daily and weekly staffing use the selected scenario without leaking the other workload', () => {
  const plan = {
    weekStart: '2026-09-14',
    columns: [{ position: 0, label: 'Monday', targetSales: 1000, totalHours: 99 }, { position: 6, label: 'Sunday', targetSales: 500 }],
    rows: [{ hour: 12, values: [
      { position: 0, isOpen: true, deliveries: 6, pizzas: 40, demand: 2, insideDemand: 3 },
      { position: 6, isOpen: true, deliveries: 9, pizzas: 20, demand: 4, insideDemand: 2 },
    ] }],
  }
  const summary = calculateDemandSummary(plan)
  assert.equal(summary.days[0].requiredStaffHours, 2)
  assert.equal(summary.days[1].requiredStaffHours, 4)
  assert.equal(summary.staffHours, 6)
  assert.equal(summary.targetSales, 1500)
  assert.equal(summary.workload, 15)
  assert.equal('pizzas' in summary, false)
  assert.equal('insideHours' in summary, false)
  assert.equal('requiredInsideHours' in summary.days[0], false)
})

test('missing demand plans and closed hours show no staff hours', () => {
  assert.deepEqual(calculateDemandSummary(null), { days: [], targetSales: 0, staffHours: 0, workload: 0 })
  const summary = calculateDemandSummary({ weekStart: '2026-09-14',
    columns: [{ position: 0, label: 'Monday', targetSales: 0 }],
    rows: [{ hour: 12, values: [{ position: 0, isOpen: false, demand: 1, deliveries: 5 }] }],
  })
  assert.equal(summary.staffHours, 0)
  assert.equal(summary.targetSales, 0)
  assert.equal(summary.workload, 0)
})

test('planned labour weights driver pay by automatic approximate hours', () => {
  const plan = {
    labourEstimate: {
      isAvailable: true,
      weightedAverageHourlyRate: 17.5,
      totalApproximateHours: 40,
      unallocatedDemandHours: 0,
      message: 'Automatic mix',
      drivers: [
        { employeeId: 'one', employeeName: 'One', approximateHours: 10, hourlyRate: 10 },
        { employeeId: 'two', employeeName: 'Two', approximateHours: 30, hourlyRate: 20 },
      ],
    },
    columns: [
      { position: 0, label: 'Monday', targetSales: 100 },
      { position: 5, label: 'Saturday', targetSales: 100 },
      { position: 6, label: 'Sunday', targetSales: 100 },
    ],
    rows: [
      { hour: 12, values: [
        { position: 0, isOpen: true, demand: 2 },
        { position: 5, isOpen: true, demand: 0 },
        { position: 6, isOpen: true, demand: 1 },
      ] },
      { hour: 1, values: [
        { position: 0, isOpen: true, demand: 0 },
        { position: 5, isOpen: true, demand: 2 },
        { position: 6, isOpen: true, demand: 1 },
      ] },
    ],
  }
  const employees = [
    { isActive: true, roles: ['Driver'], hourlyRate: 10 },
    { isActive: true, roles: ['Driver'], hourlyRate: 20 },
    { isActive: false, roles: ['Driver'], hourlyRate: 100 },
    { isActive: true, roles: ['Driver', 'Manager'], hourlyRate: 100 },
  ]

  const labour = calculateDemandLabour(plan, employees)

  assert.equal(labour.eligibleEmployeeCount, 2)
  assert.equal(labour.averageHourlyRate, 17.5)
  assert.equal(labour.usesApproximateHours, true)
  assert.equal(labour.approximateHours, 40)
  assert.equal(labour.approximateHoursCurrent, true)
  assert.equal(labour.staffHours, 6)
  assert.equal(labour.days[0].labourCost, 35)
  assert.equal(labour.days[1].sundayPremiumHours, 2)
  assert.equal(labour.days[1].labourCost, 43.75)
  assert.equal(labour.days[2].sundayPremiumHours, 1)
  assert.equal(labour.days[2].labourCost, 39.38)
  assert.equal(labour.labourCost, 118.13)
  assert.equal(labour.labourPercentage, 39.38)

  const stale = calculateDemandLabour({ ...plan, labourEstimateCurrent: false }, employees)
  assert.equal(stale.approximateHoursCurrent, false)
})

test('inside labour remains independent from a driver labour estimate', () => {
  const labour = calculateDemandLabour({
    demandKind: 'inside',
    pizzasPerInsideHour: 20,
    labourEstimate: {
      isAvailable: true, weightedAverageHourlyRate: 99, totalApproximateHours: 50,
      drivers: [{ employeeId: 'driver', approximateHours: 50, hourlyRate: 99 }],
    },
    columns: [{ position: 0, label: 'Monday', targetSales: 100 }],
    rows: [{ hour: 12, values: [{ position: 0, isOpen: true, pizzas: 20, insideDemand: 1 }] }],
  }, [
    { isActive: true, roles: ['InStore'], hourlyRate: 12 },
    { isActive: true, roles: ['Manager'], hourlyRate: 18 },
  ])

  assert.equal(labour.usesApproximateHours, false)
  assert.equal(labour.staffHours, 1)
  assert.equal(labour.workload, 20)
  assert.equal(labour.idealStaffHours, 1)
  assert.equal(labour.eligibleEmployeeCount, 2)
  assert.deepEqual(labour.approximateEmployees, [])
  assert.equal(labour.averageHourlyRate, 15)
  assert.equal(labour.labourCost, 15)
})

test('hourly labour analysis compares entered whole-driver demand with the fractional productivity ideal', () => {
  const plan = {
    deliveriesPerDriverHour: 2.7,
    columns: [
      { position: 0, label: 'Monday', targetSales: 100 },
      { position: 5, label: 'Saturday', targetSales: 100 },
    ],
    rows: [
      { hour: 12, values: [
        { position: 0, isOpen: true, deliveries: 5.4, demand: 2 },
      ] },
      { hour: 1, values: [
        { position: 0, isOpen: true, deliveries: 2.7, demand: 2 },
        { position: 5, isOpen: true, deliveries: 2.7, demand: 1 },
      ] },
    ],
  }
  const employees = [
    { isActive: true, roles: ['Driver'], hourlyRate: 10 },
  ]

  const labour = calculateDemandLabour(plan, employees)
  const monday = labour.days[0]
  const matchedHour = monday.hourly.find((hour) => hour.hour === 12)
  const aboveHour = monday.hourly.find((hour) => hour.hour === 1)
  const saturdayAfterMidnight = labour.days[1].hourly[0]

  assert.equal(labour.staffHours, 5)
  assert.equal(labour.idealStaffHours, 4)
  assert.equal(labour.wholeStaffHours, 4)
  assert.equal(labour.staffHourDifference, 1)
  assert.equal(labour.workloadCapacity, 13.5)
  assert.equal(labour.capacityUtilization, 80)
  assert.equal(labour.labourCost, 52.5)
  assert.equal(labour.idealLabourCost, 42.5)
  assert.equal(labour.labourCostDifference, 10)
  assert.equal(labour.labourCostPerUnit, 4.86)
  assert.equal(monday.workload, 8.1)
  assert.equal(monday.openHours, 2)
  assert.equal(monday.capacityUtilization, 75)
  assert.equal(monday.matchedHours, 1)
  assert.equal(monday.aboveMinimumHours, 1)
  assert.equal(monday.understaffedHours, 0)
  assert.equal(matchedHour.idealStaff, 2)
  assert.equal(matchedHour.wholeStaff, 2)
  assert.equal(matchedHour.staffingStatus, 'matched')
  assert.equal(aboveHour.idealStaff, 1)
  assert.equal(aboveHour.workloadCapacity, 5.4)
  assert.equal(aboveHour.capacityUtilization, 50)
  assert.equal(aboveHour.labourCostDifference, 10)
  assert.equal(aboveHour.staffingStatus, 'above')
  assert.equal(saturdayAfterMidnight.isSundayPremium, true)
  assert.equal(saturdayAfterMidnight.demandLabourCost, 12.5)

  const understaffed = calculateDemandLabour({
    deliveriesPerDriverHour: 2.7,
    columns: [{ position: 0, label: 'Monday', targetSales: 0 }],
    rows: [{ hour: 12, values: [{ position: 0, isOpen: true, deliveries: 5.4, demand: 1 }] }],
  }, employees).days[0]
  assert.equal(understaffed.understaffedHours, 1)
  assert.equal(understaffed.hourly[0].staffingStatus, 'under')
  assert.equal(understaffed.hourly[0].staffDifference, -1)
  assert.equal(understaffed.hourly[0].unusedWorkloadCapacity, -2.7)
  assert.equal(understaffed.hourly[0].capacityUtilization, 200)
})

test('planned labour is unavailable without eligible driver rates or complete demand', () => {
  const plan = {
    columns: [{ position: 0, label: 'Monday', targetSales: 100 }],
    rows: [{ hour: 12, values: [{ position: 0, isOpen: true, demand: null }] }],
  }

  assert.equal(calculateDemandLabour(plan, []).labourCost, null)
  const incomplete = calculateDemandLabour(plan, [
    { isActive: true, roles: ['Driver'], hourlyRate: 14.5 },
  ])
  assert.equal(incomplete.isComplete, false)
  assert.equal(incomplete.labourCost, null)
  assert.equal(incomplete.idealComplete, false)
  assert.equal(incomplete.idealLabourCost, null)

  const unavailableAutomatic = calculateDemandLabour({
    ...plan,
    labourEstimate: {
      isAvailable: false, weightedAverageHourlyRate: null, drivers: [],
      totalApproximateHours: 0, unallocatedDemandHours: 1, message: 'No useful availability',
    },
  }, [{ isActive: true, roles: ['Driver'], hourlyRate: 14.5 }])
  assert.equal(unavailableAutomatic.usesApproximateHours, true)
  assert.equal(unavailableAutomatic.averageHourlyRate, null)
  assert.equal(unavailableAutomatic.labourCost, null)
})
