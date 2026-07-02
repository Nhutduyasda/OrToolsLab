(function () {
  'use strict';

  const fmtTime = (min) => {
    const h = Math.floor(min / 60);
    const m = min % 60;
    return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
  };

  const ROUTE_COLORS = ['#2563EB', '#DC2626', '#16A34A', '#D97706', '#7C3AED', '#0891B2', '#BE185D', '#4B5563'];

  const STATUS_OPTIONS = ['Available', 'Maintenance', 'OnTrip'];
  const FLEET_STORAGE_KEY = 'ortools-lab-fleet-v1';

  function parseAndCamelCase(jsonStr) {
    try {
      const arr = JSON.parse(jsonStr || '[]');
      if (!Array.isArray(arr)) return arr;
      return arr.map(item => {
        if (item !== null && typeof item === 'object' && !Array.isArray(item)) {
          const obj = {};
          for (const key in item) {
            obj[key.charAt(0).toLowerCase() + key.slice(1)] = item[key];
          }
          return obj;
        }
        return item;
      });
    } catch (e) {
      return [];
    }
  }

  let orders = [];
  let warehouseData = null;
  let fleetMeta = { total: 10, available: 10, minSize: 1, maxSize: 50 };
  let map = null;
  let mapLayerGroup = null;
  let currentVehicleLayers = {};
  let activeVehicleMapFilter = null;

  function filterMapByVehicle(vid) {
    if (!mapLayerGroup) return;
    
    if (activeVehicleMapFilter === vid) {
      activeVehicleMapFilter = null;
    } else {
      activeVehicleMapFilter = vid;
    }
    
    Object.entries(currentVehicleLayers).forEach(([id, layer]) => {
      if (!activeVehicleMapFilter || activeVehicleMapFilter === id) {
        if (!mapLayerGroup.hasLayer(layer)) mapLayerGroup.addLayer(layer);
      } else {
        if (mapLayerGroup.hasLayer(layer)) mapLayerGroup.removeLayer(layer);
      }
    });

    const legendEl = document.getElementById('map-legend');
    if (legendEl) {
      legendEl.querySelectorAll('.legend-item').forEach(l => {
        if (!activeVehicleMapFilter) {
          l.style.opacity = '1';
          l.style.fontWeight = 'normal';
        } else if (l.dataset.vid === activeVehicleMapFilter) {
          l.style.opacity = '1';
          l.style.fontWeight = 'bold';
        } else {
          l.style.opacity = '0.4';
          l.style.fontWeight = 'normal';
        }
      });
    }

    const cardsEl = document.getElementById('vehicle-cards');
    if (cardsEl) {
      cardsEl.querySelectorAll('.vehicle-card').forEach(c => {
        if (!activeVehicleMapFilter) {
          c.style.opacity = '1';
          c.style.borderColor = 'transparent';
        } else if (c.dataset.vid === activeVehicleMapFilter) {
          c.style.opacity = '1';
          c.style.borderColor = '#2563EB';
        } else {
          c.style.opacity = '0.5';
          c.style.borderColor = 'transparent';
        }
      });
    }
  }

  function saveFleetConfig(items) {
    try {
      const cfg = {
        size: fleetMeta.total,
        statuses: Object.fromEntries(items.map(v => [v.id, v.status]))
      };
      localStorage.setItem(FLEET_STORAGE_KEY, JSON.stringify(cfg));
    } catch { /* private mode / quota */ }
  }

  async function restoreFleetFromStorage() {
    let raw;
    try { raw = localStorage.getItem(FLEET_STORAGE_KEY); } catch { return loadVehicles(); }
    if (!raw) return loadVehicles();

    try {
      const cfg = JSON.parse(raw);
      if (!cfg?.size) return loadVehicles();
      const res = await fetch('/api/fleet/restore', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ count: cfg.size, statuses: cfg.statuses || {} })
      });
      if (!res.ok) return loadVehicles();
      const data = await res.json();
      fleetMeta = data.fleet;
      renderVehicles(data.items);
      syncFleetControls();
      saveFleetConfig(data.items);
      return data.items;
    } catch {
      return loadVehicles();
    }
  }

  async function loadVehicles() {
    const data = await fetch('/api/vehicles').then(r => r.json());
    fleetMeta = data.fleet;
    renderVehicles(data.items);
    syncFleetControls();
    saveFleetConfig(data.items);
    return data.items;
  }

  async function load() {
    const [vehicles, ordersData, warehouse] = await Promise.all([
      restoreFleetFromStorage(),
      fetch('/api/orders').then(r => r.json()),
      fetch('/api/warehouse').then(r => r.json())
    ]);
    orders = ordersData;
    warehouseData = warehouse;
    renderOrders(orders);
    renderWarehouse(warehouse);
    document.getElementById('load-capacity').value = String(warehouse.concurrentLoadCapacity);
    await loadHistory();
  }

  async function loadHistory() {
    const section = document.getElementById('history-section');
    try {
      const plans = await fetch('/api/plans').then(r => r.json());
      if (plans && plans.length > 0) {
        section.hidden = false;
        const tbody = document.querySelector('#tbl-history tbody');
        tbody.innerHTML = plans.map(p => `
          <tr style="cursor:pointer" onclick="window.viewHistoryPlan('${p.id}')">
            <td>${new Date(p.createdAt).toLocaleString()}</td>
            <td>${p.success ? '✅ Thành công' : '❌ Thất bại'}</td>
            <td>${p.vehiclesUsed}</td>
            <td>${(p.totalDistanceM / 1000).toFixed(1)} km</td>
            <td>${p.message}</td>
          </tr>
        `).join('');
      } else {
        section.hidden = true;
      }
    } catch (e) {
      console.error('Failed to load history', e);
    }
  }

  window.viewHistoryPlan = async function(id) {
    try {
      const plan = await fetch(`/api/plans/${id}`).then(r => r.json());
      if (plan) {
        plan.vehicleSummaries = parseAndCamelCase(plan.vehicleSummariesJson);
        plan.optimizationInsights = parseAndCamelCase(plan.optimizationInsightsJson);
        plan.dockSchedule = parseAndCamelCase(plan.dockScheduleJson);
        plan.validations = parseAndCamelCase(plan.validationsJson);
        plan.convoyChecks = parseAndCamelCase(plan.convoyChecksJson);
        await renderResult(plan, plan.id);
        
        // Hide confirm button since it's history
        document.getElementById('btn-confirm-plan').style.display = 'none';
        document.getElementById('btn-export-excel').style.display = 'inline-block';
        document.getElementById('btn-export-pdf').style.display = 'inline-block';
      }
    } catch (e) {
      alert('Không tải được lịch sử: ' + e.message);
    }
  };

  function syncFleetControls() {
    const sizeInput = document.getElementById('fleet-size');
    const slider = document.getElementById('available-count');
    sizeInput.value = fleetMeta.total;
    sizeInput.min = fleetMeta.minSize;
    sizeInput.max = fleetMeta.maxSize;
    slider.max = fleetMeta.total;
    slider.value = fleetMeta.available;
    document.getElementById('available-count-value').textContent = fleetMeta.available;
    document.getElementById('fleet-total-label').textContent = fleetMeta.total;
    document.getElementById('fleet-summary').textContent =
      `${fleetMeta.total} xe · ${fleetMeta.available} Available (solver)`;
  }

  function renderVehicles(list) {
    const tbody = document.querySelector('#tbl-vehicles tbody');
    tbody.innerHTML = list.map(v => `
      <tr class="${v.available ? '' : 'row-inactive'}" data-id="${v.id}">
        <td><code>${v.id}</code></td>
        <td>${v.plate}</td>
        <td>
          <select class="status-select status-${v.status.toLowerCase()}" data-vehicle-id="${v.id}" aria-label="Status ${v.id}">
            ${STATUS_OPTIONS.map(s => `<option value="${s}" ${s === v.status ? 'selected' : ''}>${s}</option>`).join('')}
          </select>
        </td>
        <td>${v.maxWeightKg.toLocaleString()}</td>
        <td>${v.maxVolumeM3}</td>
        <td>${v.preference || '—'}</td>
      </tr>
    `).join('');

    tbody.querySelectorAll('.status-select').forEach(sel => {
      sel.addEventListener('change', () => onVehicleStatusChange(sel));
    });
  }

  async function onVehicleStatusChange(select) {
    const id = select.dataset.vehicleId;
    select.disabled = true;
    try {
      const res = await fetch(`/api/vehicles/${id}/status`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: select.value })
      });
      if (!res.ok) throw new Error(await res.text());
      const full = await fetch('/api/vehicles').then(r => r.json());
      fleetMeta = full.fleet;
      renderVehicles(full.items);
      syncFleetControls();
      saveFleetConfig(full.items);
    } catch (e) {
      alert('Không đổi được status: ' + e.message);
      await loadVehicles();
    } finally {
      select.disabled = false;
    }
  }

  async function applyFleetSize(count) {
    const res = await fetch('/api/fleet/size', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ count })
    });
    if (!res.ok) throw new Error(await res.text());
    const data = await res.json();
    fleetMeta = data.fleet;
    renderVehicles(data.items);
    syncFleetControls();
    saveFleetConfig(data.items);
  }

  async function applyAvailableCount(count) {
    const res = await fetch('/api/fleet/available-count', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ count })
    });
    if (!res.ok) throw new Error(await res.text());
    const data = await res.json();
    fleetMeta = data.fleet;
    renderVehicles(data.items);
    syncFleetControls();
    saveFleetConfig(data.items);
  }

  document.getElementById('btn-fleet-apply').addEventListener('click', async () => {
    const n = parseInt(document.getElementById('fleet-size').value, 10);
    try { await applyFleetSize(n); } catch (e) { alert(e.message); }
  });

  document.querySelectorAll('.btn-preset').forEach(btn => {
    btn.addEventListener('click', async () => {
      const n = parseInt(btn.dataset.size, 10);
      document.getElementById('fleet-size').value = n;
      try { await applyFleetSize(n); } catch (e) { alert(e.message); }
    });
  });

  document.getElementById('available-count').addEventListener('input', e => {
    document.getElementById('available-count-value').textContent = e.target.value;
  });

  document.getElementById('available-count').addEventListener('change', async e => {
    try { await applyAvailableCount(parseInt(e.target.value, 10)); }
    catch (err) { alert(err.message); await loadVehicles(); }
  });

  document.getElementById('btn-all-available').addEventListener('click', async () => {
    try { await applyAvailableCount(fleetMeta.total); }
    catch (e) { alert(e.message); }
  });

  function renderOrders(list) {
    const tbody = document.querySelector('#tbl-orders tbody');
    tbody.innerHTML = list.map(o => `
      <tr>
        <td><input type="checkbox" class="order-chk" value="${o.id}" data-code="${o.orderCode}"></td>
        <td><code>${o.id}</code></td>
        <td><strong>${o.orderCode}</strong></td>
        <td>${o.weightKg}</td>
        <td>${o.volumeM3}</td>
        <td>${o.lat}</td>
        <td>${o.lng}</td>
      </tr>
    `).join('');
    updateCount();
  }

  function renderWarehouse(w) {
    document.getElementById('warehouse-info').innerHTML = `
      <div><strong>${w.name}</strong> (<code>${w.id}</code>)</div>
      <div>Tọa độ: ${w.lat}, ${w.lng}</div>
      <div>Bốc đồng thời: <strong>${w.concurrentLoadCapacity}</strong> xe/lần · Thời gian bốc: ${w.loadTimeMinutes} phút</div>
    `;
  }

  function selectedIds() {
    return [...document.querySelectorAll('.order-chk:checked')].map(c => c.value);
  }

  function updateCount() {
    document.getElementById('selected-count').textContent = `${selectedIds().length} đã chọn`;
  }

  document.getElementById('tbl-orders').addEventListener('change', e => {
    if (e.target.classList.contains('order-chk')) updateCount();
  });

  document.getElementById('chk-all').addEventListener('change', e => {
    document.querySelectorAll('.order-chk').forEach(c => { c.checked = e.target.checked; });
    updateCount();
  });

  document.getElementById('btn-select-all').addEventListener('click', () => {
    document.querySelectorAll('.order-chk').forEach(c => { c.checked = true; });
    document.getElementById('chk-all').checked = true;
    updateCount();
  });

  document.getElementById('btn-clear').addEventListener('click', () => {
    document.querySelectorAll('.order-chk').forEach(c => { c.checked = false; });
    document.getElementById('chk-all').checked = false;
    updateCount();
  });

  document.getElementById('btn-select-code').addEventListener('click', () => {
    document.querySelectorAll('.order-chk').forEach(c => {
      c.checked = c.dataset.code === 'ORD-001';
    });
    updateCount();
  });

  document.getElementById('load-capacity').addEventListener('change', async e => {
    const cap = parseInt(e.target.value, 10);
    const w = await fetch('/api/warehouse/load-capacity', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ concurrentLoadCapacity: cap })
    }).then(r => r.json());
    renderWarehouse(w);
  });

  document.getElementById('balance-level').addEventListener('input', e => {
    document.getElementById('balance-value').textContent = e.target.value;
  });

  document.getElementById('btn-plan').addEventListener('click', async () => {
    const ids = selectedIds();
    if (!ids.length) {
      alert('Hãy chọn ít nhất một đơn hàng.');
      return;
    }

    const btn = document.getElementById('btn-plan');
    btn.disabled = true;
    btn.textContent = 'Đang giải…';

    const balanceLevel = parseInt(document.getElementById('balance-level').value, 10);

    try {
      const response = await fetch('/api/plan', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ selectedOrderIds: ids, balanceLevel })
      }).then(r => r.json());
      let r = response.result;
      r.vehicleSummaries = parseAndCamelCase(r.vehicleSummariesJson);
      r.optimizationInsights = parseAndCamelCase(r.optimizationInsightsJson);
      r.dockSchedule = parseAndCamelCase(r.dockScheduleJson);
      r.validations = parseAndCamelCase(r.validationsJson);
      r.convoyChecks = parseAndCamelCase(r.convoyChecksJson);
      await renderResult(r, response.planId);
    } finally {
      btn.disabled = false;
      btn.textContent = 'Lập kế hoạch';
    }
  });

  let currentPlanId = null;

  async function renderResult(r, planId) {
    currentPlanId = planId;
    const section = document.getElementById('result-section');
    section.hidden = false;

    const btnConfirm = document.getElementById('btn-confirm-plan');
    const btnExcel = document.getElementById('btn-export-excel');
    const btnPdf = document.getElementById('btn-export-pdf');

    if (r.success) {
      btnConfirm.style.display = 'inline-block';
      btnExcel.style.display = 'none'; // Only available after confirm
      btnPdf.style.display = 'none';
      btnConfirm.disabled = false;
      btnConfirm.textContent = 'Xác nhận kế hoạch';
    } else {
      btnConfirm.style.display = 'none';
      btnExcel.style.display = 'none';
      btnPdf.style.display = 'none';
    }

    const msg = document.getElementById('plan-message');
    msg.textContent = r.message;
    msg.className = 'message ' + (r.success ? 'ok' : 'fail');

    document.getElementById('plan-stats').innerHTML = r.success ? `
      <span>Fleet: <strong>${r.fleetAvailable ?? '—'}/${r.fleetTotal ?? '—'} Available</strong></span>
      <span>Xe dùng: <strong>${r.vehiclesUsed}</strong></span>
      <span>Điểm giao: <strong>${r.stops.length}</strong></span>
      <span>Tổng km: <strong>${(r.totalDistanceM / 1000).toFixed(1)}</strong></span>
      <span>Ma trận: <strong>${r.usedRoadNetwork ? 'OSRM (đường thực)' : 'Chim bay'}</strong></span>
      <span>Cân bằng: <strong>${r.balanceLevel ?? '—'}%</strong></span>
      ${r.estimatedNaiveDistanceM ? `<span>Greedy NN: <strong>${(r.estimatedNaiveDistanceM / 1000).toFixed(1)} km</strong></span>` : ''}
    ` : '';

    renderVehicleCards(r.vehicleSummaries || []);
    renderOptimizationInsights(r.optimizationInsights || []);

    document.querySelector('#tbl-plan tbody').innerHTML = (r.stops || []).map(s => `
      <tr>
        <td>${s.sequence}</td>
        <td><code>${s.vehicleId}</code></td>
        <td>${s.vehiclePlate}</td>
        <td><code>${s.orderId}</code></td>
        <td><strong>${s.orderCode}</strong></td>
        <td>${fmtTime(s.departWarehouseMin)} <small>(${s.departWarehouseMin})</small></td>
        <td>${fmtTime(s.arrivalMin)} <small>(${s.arrivalMin})</small></td>
        <td>${s.weightKg}</td>
        <td>${s.volumeM3}</td>
        <td>${s.lat}</td>
        <td>${s.lng}</td>
      </tr>
    `).join('');

    document.querySelector('#tbl-dock tbody').innerHTML = (r.dockSchedule || [])
      .filter(d => d.used)
      .map(d => `
      <tr>
        <td><code>${d.vehicleId}</code></td>
        <td>${d.vehiclePlate}</td>
        <td>${fmtTime(d.loadStartMin)} <small>(${d.loadStartMin})</small></td>
        <td>${fmtTime(d.loadEndMin)} <small>(${d.loadEndMin})</small></td>
        <td>${d.used ? '✓' : '—'}</td>
      </tr>
    `).join('') || '<tr><td colspan="5" style="color:var(--muted)">—</td></tr>';

    document.querySelector('#tbl-validation tbody').innerHTML = (r.validations || []).map(v => `
      <tr>
        <td>${v.rule}</td>
        <td>${v.detail}</td>
        <td class="${v.passed ? 'pass' : 'fail'}">${v.passed ? '✓ Đạt' : '✗ Vi phạm'}</td>
      </tr>
    `).join('');

    document.querySelector('#tbl-convoy tbody').innerHTML = (r.convoyChecks || []).map(c => `
      <tr>
        <td><strong>${c.orderCode}</strong></td>
        <td>${c.vehicleIds.join(', ')}</td>
        <td>${c.orderIds.join(', ')}</td>
        <td>${fmtTime(c.departWarehouseMin)}</td>
        <td>${Object.entries(c.arrivalByOrderId).map(([id, t]) => `${id}: ${fmtTime(t)}`).join('<br>')}</td>
        <td class="${c.sameDeparture ? 'pass' : 'fail'}">${c.sameDeparture ? '✓' : '✗'}</td>
        <td class="${c.sameArrival ? 'pass' : 'fail'}">${c.sameArrival ? '✓' : '✗'}</td>
      </tr>
    `).join('') || '<tr><td colspan="7" style="color:var(--muted)">Không có convoy (cùng mã trên ≥2 xe)</td></tr>';

    await renderMap(r.stops || []);
    section.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  function renderVehicleCards(summaries) {
    const el = document.getElementById('vehicle-cards');
    if (!summaries.length) {
      el.hidden = true;
      return;
    }
    el.hidden = false;
    el.innerHTML = summaries.map(s => `
      <div class="vehicle-card" data-vid="${s.vehicleId}" style="cursor:pointer; border: 2px solid transparent; transition: all 0.2s;">
        <div class="plate">${s.vehiclePlate}</div>
        <div class="metric"><strong>${s.orderCount}</strong> đơn giao</div>
        <div class="metric">${s.stopCount} điểm dừng · ${(s.routeDistanceM / 1000).toFixed(1)} km</div>
        <div class="metric">Tải: ${s.totalWeightKg}/${s.maxWeightKg} kg</div>
        <div class="bar-wrap" title="Sử dụng tải trọng ${s.weightUtilizationPct}%">
          <div class="bar-fill" style="width:${Math.min(s.weightUtilizationPct, 100)}%"></div>
        </div>
      </div>
    `).join('');

    el.querySelectorAll('.vehicle-card').forEach(c => {
      c.addEventListener('click', () => filterMapByVehicle(c.dataset.vid));
    });
  }

  function renderOptimizationInsights(insights) {
    const el = document.getElementById('optimization-insights');
    if (!insights.length) {
      el.hidden = true;
      return;
    }
    el.hidden = false;
    el.innerHTML = insights.map(i => `
      <div class="insight-item ${i.positive ? 'positive' : 'neutral'}">
        <span class="insight-title">${i.title}</span>
        <span>${i.detail}</span>
      </div>
    `).join('');
  }

  async function fetchRoadRoute(latlngs) {
    if (latlngs.length < 2) return latlngs;
    const coords = latlngs.map(([lat, lng]) => `${lng},${lat}`).join(';');
    const url = `https://router.project-osrm.org/route/v1/driving/${coords}?overview=full&geometries=geojson`;
    try {
      const res = await fetch(url);
      const data = await res.json();
      if (data.code !== 'Ok' || !data.routes?.[0]?.geometry?.coordinates)
        return latlngs;
      return data.routes[0].geometry.coordinates.map(([lng, lat]) => [lat, lng]);
    } catch {
      return latlngs;
    }
  }

  function initMap() {
    if (map) return;
    const wh = warehouseData || { lat: 10.8411, lng: 106.809 };
    map = L.map('map').setView([wh.lat, wh.lng], 11);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 18,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);
    mapLayerGroup = L.layerGroup().addTo(map);
  }

  async function renderMap(stops) {
    const mapEl = document.getElementById('map');
    const legendEl = document.getElementById('map-legend');
    const statusEl = document.getElementById('map-status');

    if (!stops.length || !warehouseData) {
      mapEl.hidden = true;
      legendEl.hidden = true;
      statusEl.hidden = true;
      return;
    }

    mapEl.hidden = false;
    legendEl.hidden = false;
    statusEl.hidden = false;
    statusEl.textContent = 'Đang tải tuyến đường thực tế (OSRM)…';
    initMap();
    mapLayerGroup.clearLayers();
    currentVehicleLayers = {};
    activeVehicleMapFilter = null;

    const whLat = warehouseData.lat;
    const whLng = warehouseData.lng;

    L.marker([whLat, whLng])
      .bindPopup(`<b>${warehouseData.name}</b><br>Kho bốc hàng`)
      .addTo(mapLayerGroup);

    const byVehicle = {};
    stops.forEach(s => {
      if (!byVehicle[s.vehicleId]) {
        byVehicle[s.vehicleId] = { plate: s.vehiclePlate, stops: [] };
      }
      byVehicle[s.vehicleId].stops.push(s);
    });

    const bounds = L.latLngBounds([[whLat, whLng]]);
    const legendItems = [];
    let roadOk = 0;
    let idx = 0;

    for (const [vId, data] of Object.entries(byVehicle)) {
      const color = ROUTE_COLORS[idx % ROUTE_COLORS.length];
      idx++;
      const ordered = [...data.stops].sort((a, b) => a.arrivalMin - b.arrivalMin);
      const uniquePoints = [];
      const seen = new Set();
      for (const s of ordered) {
        const key = `${s.lat},${s.lng}`;
        if (!seen.has(key)) {
          seen.add(key);
          uniquePoints.push(s);
        }
      }
      const waypoints = [[whLat, whLng], ...uniquePoints.map(s => [s.lat, s.lng])];
      const roadPath = await fetchRoadRoute(waypoints);
      if (roadPath.length > waypoints.length) roadOk++;

      const vLayer = L.layerGroup();
      currentVehicleLayers[vId] = vLayer;

      L.polyline(roadPath, { color, weight: 5, opacity: 0.85 }).addTo(vLayer);

      uniquePoints.forEach((s, i) => {
        bounds.extend([s.lat, s.lng]);
        const count = ordered.filter(o => o.lat === s.lat && o.lng === s.lng).length;
        L.circleMarker([s.lat, s.lng], {
          radius: 8 + Math.min(count, 5),
          color,
          fillColor: color,
          fillOpacity: 0.85,
          weight: 2
        })
          .bindPopup(`
            <b>${s.orderCode}</b>${count > 1 ? ` (${count} đơn)` : ''}<br>
            Xe: ${s.vehiclePlate}<br>
            Đến: ${fmtTime(s.arrivalMin)}
          `)
          .addTo(vLayer);

        L.marker([s.lat, s.lng], {
          icon: L.divIcon({
            className: 'stop-label',
            html: `<span style="background:${color};color:#fff;padding:1px 5px;border-radius:4px;font-size:10px;font-weight:600">${i + 1}</span>`,
            iconSize: [20, 16],
            iconAnchor: [10, 8]
          })
        }).addTo(vLayer);
      });

      vLayer.addTo(mapLayerGroup);

      legendItems.push(`<span class="legend-item" data-vid="${vId}" style="cursor:pointer; padding:2px 6px; border-radius:4px; transition: all 0.2s;"><span style="background:${color};display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:4px"></span>${data.plate}</span>`);
    }

    legendEl.innerHTML = legendItems.join(' ');
    legendEl.querySelectorAll('.legend-item').forEach(el => {
      el.addEventListener('click', () => filterMapByVehicle(el.dataset.vid));
    });
    statusEl.textContent = roadOk > 0
      ? `Tuyến theo đường thực tế (OSRM / OpenStreetMap) — ${roadOk}/${Object.keys(byVehicle).length} xe`
      : 'Không tải được OSRM — hiển thị đường chim bay';
    map.fitBounds(bounds.pad(0.12));
    setTimeout(() => map.invalidateSize(), 300);
  }

  document.getElementById('btn-confirm-plan').addEventListener('click', async () => {
    if (!currentPlanId) return;
    const btn = document.getElementById('btn-confirm-plan');
    btn.disabled = true;
    btn.textContent = 'Đang xác nhận...';
    
    try {
      const res = await fetch(`/api/plan/${currentPlanId}/confirm`, { method: 'POST' });
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      alert(data.message);
      
      btn.style.display = 'none';
      document.getElementById('btn-export-excel').style.display = 'inline-block';
      document.getElementById('btn-export-pdf').style.display = 'inline-block';
      await loadHistory();
    } catch (e) {
      alert('Lỗi: ' + e.message);
      btn.disabled = false;
      btn.textContent = 'Xác nhận kế hoạch';
    }
  });

  document.getElementById('btn-export-excel').addEventListener('click', () => {
    if (currentPlanId) window.open(`/api/orders/export/excel?planId=${currentPlanId}`, '_blank');
  });

  document.getElementById('btn-export-pdf').addEventListener('click', () => {
    if (currentPlanId) window.open(`/api/orders/export/pdf?planId=${currentPlanId}`, '_blank');
  });

  load();
})();
