/** Раскрывает прежние объединённые разрешения без потери доступа. */
export function expandPermissions(permissions = []) {
  const selected = new Set(permissions)
  if (selected.delete('vehicles.works')) {
    for (const key of ['vehicles.repair', 'vehicles.repair_history', 'vehicles.maintenance_execution']) selected.add(key)
  }
  if (selected.delete('vehicles.report')) {
    selected.add('reports.shift')
    selected.add('reports.gto')
  }
  if ([...selected].some(key => key.startsWith('vehicles.'))) selected.add('menu.vehicles')
  if (selected.has('reports.shift') || selected.has('reports.gto')) {
    selected.add('menu.reports')
    selected.add('menu.vehicles')
  }
  if (selected.has('menu.requirements')) selected.add('menu.reports')
  return [...selected]
}
