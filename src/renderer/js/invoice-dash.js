// "Pano": the Faturalar dashboard. Monthly (one year at a time) or yearly
// totals as a column chart with its table, the customers billed most, and
// how long each test system was in use. Clicking a month or year scopes the
// figures, customers and devices to it; picking a customer (in the filter row
// or its bar) scopes everything to that customer. Everything below the
// filter row always shows the same slice.

import { icon } from './icons.js';
import { esc } from './ui.js';
import { todayStr } from '../../shared/store.js';
import { formatEuro } from '../../shared/invoices.js';
import { MONTHS, customerSummary, deviceUsage, invoiceYears, monthlySummary, periodRange, periodSummary, undatedInvoices, yearlySummary } from '../../shared/invoice-stats.js';
import { customerKey, customerOf } from '../../shared/customers.js';

const MODE_KEY = 'xloer-dash-mode';
const compact = new Intl.NumberFormat('tr-TR', { notation: 'compact', maximumFractionDigits: 1 });
const whole = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat('tr-TR', { style: 'percent', maximumFractionDigits: 0 });
const shortMonth = (m) => MONTHS[m - 1].slice(0, 3);

// 0 and three or four round steps above the largest value.
function ticks(max) {
  if (max <= 0) return [0];
  const raw = max / 4;
  const mag = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map((f) => f * mag).find((s) => s >= raw);
  const out = [];
  for (let v = 0; v <= max + step * 0.001; v += step) out.push(v);
  if (out.at(-1) < max) out.push(out.at(-1) + step);
  return out;
}

export function createInvoiceDash(el) {
  let state = null;
  let mode = 'month';
  try {
    if (localStorage.getItem(MODE_KEY) === 'year') mode = 'year';
  } catch {}
  let year = null;
  let picked = null; // a month (1–12) in monthly mode, a year in yearly mode
  let tips = {};
  let allDevices = false;
  let allCustomers = false;
  let customer = null; // a customerKey, or every customer

  el.innerHTML = `
    <div class="dash-filters">
      <div class="seg" role="radiogroup" aria-label="Dönem">
        <button type="button" role="radio" data-mode="month">Aylık</button>
        <button type="button" role="radio" data-mode="year">Yıllık</button>
      </div>
      <div class="dash-year">
        <button type="button" class="icon-btn small" data-step="-1" title="Önceki yıl" aria-label="Önceki yıl"><span class="flip">${icon('chevronRight', 15)}</span></button>
        <b id="dash-year"></b>
        <button type="button" class="icon-btn small" data-step="1" title="Sonraki yıl" aria-label="Sonraki yıl">${icon('chevronRight', 15)}</button>
      </div>
      <label class="dash-customer"><span>Müşteri</span><select id="dash-customer"></select></label>
      <span class="dash-scope" id="dash-scope"></span>
    </div>
    <div class="dash-body scroll" id="dash-body"></div>
    <div class="viz-tip" role="tooltip" hidden></div>`;
  const body = el.querySelector('#dash-body');
  const tip = el.querySelector('.viz-tip');

  // The invoices the filter row leaves: one customer's, or all.
  const pool = () => (customer ? state.invoices.filter((i) => customerKey(customerOf(i)) === customer) : state.invoices);

  function scope() {
    if (mode === 'month') {
      const { from, to } = periodRange(year, picked);
      return { from, to, label: picked ? `${MONTHS[picked - 1]} ${year}` : String(year) };
    }
    if (picked) return { ...periodRange(picked), label: String(picked) };
    // "Tüm yıllar" spans every year on record, whoever is picked.
    const years = invoiceYears(state.invoices);
    return years.length ? { from: `${years.at(-1)}-01-01`, to: `${years[0]}-12-31`, label: 'Tüm yıllar' } : { from: '9999-01-01', to: '9999-12-31', label: 'Tüm yıllar' };
  }

  // Customers by amount in the period, the picked one marked.
  function customersHtml(list) {
    if (!list.length) return '<p class="dash-empty">Bu dönemde kesilen fatura yok.</p>';
    const max = Math.max(...list.map((c) => c.amount), 1);
    const shown = allCustomers ? list : list.slice(0, 10);
    const more = list.length > 10 ? `<button type="button" class="link-btn hbar-more" data-all-customers>${allCustomers ? 'İlk 10 müşteri' : `Tüm müşteriler (${list.length})`}</button>` : '';
    return `<div class="hbar-list">${shown
      .map(
        (c, i) => `<button type="button" class="hbar${customer === c.key ? ' on' : ''}${customer && customer !== c.key ? ' dim' : ''}" data-customer="${esc(c.key)}" data-tip="c${i}" aria-pressed="${customer === c.key}" aria-label="${esc(`${c.name}: ${formatEuro(c.amount)}, ${c.projects} proje`)}">
          <span class="hbar-head"><span class="hbar-name" title="${esc(c.name)}">${esc(c.name)}</span><span class="hbar-value">${esc(formatEuro(c.amount))} · ${c.projects} proje</span></span>
          <span class="hbar-bar" style="width:${(c.amount / max) * 100}%"></span>
        </button>`,
      )
      .join('')}</div>${more}`;
  }

  function kpisHtml(s, label) {
    return `<section class="dash-kpis" aria-label="${esc(label)} özeti">
      <div class="kpi hero"><span class="kpi-label">Kesilen tutar · ${esc(label)}</span><b class="kpi-value">${esc(formatEuro(s.amount))}</b></div>
      <div class="kpi"><span class="kpi-label">Proje</span><b class="kpi-value">${whole.format(s.projects)}</b></div>
      <div class="kpi"><span class="kpi-label">Test günü</span><b class="kpi-value">${whole.format(s.days)}</b></div>
      <div class="kpi"><span class="kpi-label">Vardiya</span><b class="kpi-value">${whole.format(s.shifts)}</b></div>
    </section>`;
  }

  function columnsHtml(buckets) {
    const max = Math.max(0, ...buckets.map((b) => b.amount));
    const scale = ticks(max);
    const top = scale.at(-1) || 1;
    const peak = buckets.reduce((a, b) => (b.amount > (a?.amount ?? 0) ? b : a), null);
    const grid = scale
      .map((v) => `<div class="col-grid" style="bottom:${(v / top) * 100}%"><span>${v ? `€ ${compact.format(v)}` : '0'}</span></div>`)
      .join('');
    const cols = buckets
      .map((b, i) => {
        const key = b.month ?? b.year;
        const on = picked === key;
        const dim = picked != null && !on;
        const h = (b.amount / top) * 100;
        const showValue = b.amount > 0 && (on || (picked == null && b === peak));
        return `<button type="button" class="col${on ? ' on' : ''}${dim ? ' dim' : ''}" data-pick="${key}" data-tip="${i}" aria-pressed="${on}" aria-label="${esc(`${b.label}: ${formatEuro(b.amount)}, ${b.projects} proje`)}">
          <span class="col-bar" style="height:${h}%">${showValue ? `<span class="col-value">€ ${compact.format(b.amount)}</span>` : ''}</span>
          <span class="col-label">${esc(b.month ? shortMonth(b.month) : b.label)}</span>
        </button>`;
      })
      .join('');
    return `<div class="col-chart" style="--cols:${buckets.length}"><div class="col-plot">${grid}<div class="col-row">${cols}</div></div></div>`;
  }

  function tableHtml(buckets, total) {
    const rows = buckets
      .map((b) => {
        const key = b.month ?? b.year;
        return `<tr data-pick="${key}" class="${picked === key ? 'on' : ''}${b.amount || b.days ? '' : ' quiet'}" tabindex="0">
          <td>${esc(b.label)}</td><td class="num">${b.projects || '–'}</td><td class="num">${b.days || '–'}</td><td class="num">${b.shifts || '–'}</td><td class="num">${b.amount ? esc(formatEuro(b.amount)) : '–'}</td>
        </tr>`;
      })
      .join('');
    return `<table class="dash-table">
      <thead><tr><th>${mode === 'month' ? 'Ay' : 'Yıl'}</th><th class="num">Proje</th><th class="num">Test günü</th><th class="num">Vardiya</th><th class="num">Tutar</th></tr></thead>
      <tbody>${rows}</tbody>
      <tfoot><tr><td>Toplam</td><td class="num">${total.projects}</td><td class="num">${total.days}</td><td class="num">${total.shifts}</td><td class="num">${esc(formatEuro(total.amount))}</td></tr></tfoot>
    </table>`;
  }

  // The ten busiest systems; the rest behind a toggle.
  function devicesHtml(usage) {
    if (!usage.length) return '<p class="dash-empty">Bu dönemde tarihli test yok.</p>';
    const max = usage[0].days;
    const shown = allDevices ? usage : usage.slice(0, 10);
    const more = usage.length > 10 ? `<button type="button" class="link-btn hbar-more" data-all>${allDevices ? 'İlk 10 cihaz' : `Tüm cihazlar (${usage.length})`}</button>` : '';
    return `<div class="hbar-list">${shown
      .map(
        (d, i) => `<div class="hbar" data-tip="d${i}" tabindex="0" aria-label="${esc(`${d.device}: ${d.days} gün, ${d.shifts} vardiya`)}">
          <span class="hbar-head"><span class="hbar-name" title="${esc(d.device)}">${esc(d.device)}</span><span class="hbar-value">${d.days} gün · ${d.shifts} vardiya</span></span>
          <span class="hbar-bar" style="width:${(d.days / max) * 100}%"></span>
        </div>`,
      )
      .join('')}</div>${more}`;
  }

  function render(next) {
    if (next) state = next;
    if (!state) return;
    tip.hidden = true;
    // The customer list for the filter: everyone, biggest first.
    const everyone = customerSummary(state.invoices, '0000-01-01', '9999-12-31');
    if (customer && !everyone.some((c) => c.key === customer)) customer = null;
    const select = el.querySelector('#dash-customer');
    select.innerHTML = `<option value="">Tümü</option>${everyone.map((c) => `<option value="${esc(c.key)}"${c.key === customer ? ' selected' : ''}>${esc(c.name)}</option>`).join('')}`;
    const invoices = pool();
    const years = invoiceYears(invoices);
    // Start on this year; a mistyped future date must not pull the view there.
    if (year == null || (years.length && !years.includes(year))) {
      const now = Number(todayStr().slice(0, 4));
      year = years.includes(now) ? now : (years.find((y) => y < now) ?? years.at(-1) ?? now);
    }
    if (mode === 'year' && picked != null && !years.includes(picked)) picked = null;

    el.querySelectorAll('[data-mode]').forEach((b) => b.setAttribute('aria-checked', String(b.dataset.mode === mode)));
    el.querySelector('.dash-year').hidden = mode !== 'month';
    el.querySelector('#dash-year').textContent = year;
    el.querySelector('[data-step="-1"]').disabled = !years.some((y) => y < year);
    el.querySelector('[data-step="1"]').disabled = !years.some((y) => y > year);

    const buckets = mode === 'month' ? monthlySummary(invoices, year) : yearlySummary(invoices);
    const s = scope();
    const who = customer ? everyone.find((c) => c.key === customer)?.name : null;
    if (who) s.label = `${s.label} · ${who}`;
    const summary = periodSummary(invoices, s.from, s.to);
    const usage = deviceUsage(invoices, s.from, s.to, todayStr());
    const customers = customerSummary(state.invoices, s.from, s.to);
    const yearRange = periodRange(year);
    const total = mode === 'month' ? periodSummary(invoices, yearRange.from, yearRange.to) : summary;
    const undated = undatedInvoices(invoices);
    tips = {};
    buckets.forEach((b, i) => {
      tips[i] = { value: formatEuro(b.amount), lines: [b.month ? `${b.label} ${year}` : b.label, `${b.projects} proje · ${b.days} test günü · ${b.shifts} vardiya`] };
    });
    customers.forEach((c, i) => {
      tips[`c${i}`] = { value: formatEuro(c.amount), lines: [c.name, `${c.projects} proje · ${c.days} test günü · ${c.shifts} vardiya`] };
    });
    usage.forEach((d, i) => {
      tips[`d${i}`] = {
        value: `${d.days} gün · ${d.shifts} vardiya`,
        lines: [d.device, [d.share != null && `${percent.format(d.share)} doluluk`, `${d.tests} test`, `${d.projects} proje`].filter(Boolean).join(' · ')],
      };
    });

    const scopeEl = el.querySelector('#dash-scope');
    scopeEl.innerHTML = picked != null || customer ? `Seçili: <b>${esc(s.label)}</b> <button type="button" class="icon-btn small" data-clear title="Seçimi kaldır" aria-label="Seçimi kaldır">${icon('x', 13)}</button>` : '';

    if (!state.invoices.length) {
      body.innerHTML = '<p class="empty">Henüz proje yok. Fatura Excel’ini pencereye sürükle ya da “Projeler”den yeni proje ekle; pano kendiliğinden dolar.</p>';
      return;
    }
    const chartTitle = mode === 'month' ? `Aylık kesilen tutar · ${year}` : 'Yıllık kesilen tutar';
    body.innerHTML = `
      ${kpisHtml(summary, s.label)}
      <div class="dash-grid">
        <section class="dash-card">
          <h3>${esc(chartTitle)}</h3>
          <p class="dash-sub">Tutar, projenin son test tarihinin ${mode === 'month' ? 'ayına' : 'yılına'} yazılır. Bir ${mode === 'month' ? 'aya' : 'yıla'} tıkla: üstteki sayılar ve cihazlar ona göre süzülür.</p>
          ${columnsHtml(buckets)}
          ${tableHtml(buckets, total)}
        </section>
        <div class="dash-side">
          <section class="dash-card">
            <h3>Müşteriler · ${esc(scope().label)}</h3>
            <p class="dash-sub">Kesilen tutara göre. Bir müşteriye tıkla: pano yalnız onun projelerini gösterir.</p>
            ${customersHtml(customers)}
          </section>
          <section class="dash-card">
            <h3>Cihaz kullanımı · ${esc(s.label)}</h3>
            <p class="dash-sub">Cihazın test yaptığı takvim günleri; aynı gün iki test bir gün sayılır. Vardiya = gün × 3.</p>
            ${devicesHtml(usage)}
          </section>
        </div>
      </div>
      ${undated.projects ? `<p class="dash-note">Tarihsiz ${undated.projects} proje (${esc(formatEuro(undated.amount))}) hiçbir ${mode === 'month' ? 'aya' : 'yıla'} yazılmadı; test tarihi girildiğinde panoya eklenir.</p>` : ''}`;
  }

  function pick(key) {
    picked = picked === key ? null : key;
    render();
  }

  el.addEventListener('click', (e) => {
    const t = e.target;
    const modeBtn = t.closest('[data-mode]');
    if (modeBtn) {
      mode = modeBtn.dataset.mode;
      picked = null;
      try {
        localStorage.setItem(MODE_KEY, mode);
      } catch {}
      return render();
    }
    const step = t.closest('[data-step]');
    if (step) {
      const years = invoiceYears(pool());
      const dir = Number(step.dataset.step);
      const nextYear = dir < 0 ? years.find((y) => y < year) : [...years].reverse().find((y) => y > year);
      if (nextYear) {
        year = nextYear;
        picked = null;
        render();
      }
      return;
    }
    if (t.closest('[data-clear]')) {
      picked = null;
      customer = null;
      return render();
    }
    const who = t.closest('[data-customer]');
    if (who) {
      customer = customer === who.dataset.customer ? null : who.dataset.customer;
      return render();
    }
    if (t.closest('[data-all-customers]')) {
      allCustomers = !allCustomers;
      return render();
    }
    if (t.closest('[data-all]')) {
      allDevices = !allDevices;
      return render();
    }
    const target = t.closest('[data-pick]');
    if (target) pick(Number(target.dataset.pick));
  });

  el.querySelector('#dash-customer').addEventListener('change', (e) => {
    customer = e.target.value || null;
    render();
  });

  el.addEventListener('keydown', (e) => {
    const row = e.target.closest('tr[data-pick]');
    if (row && (e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      pick(Number(row.dataset.pick));
    }
  });

  // One tooltip for every mark: value first, then what it is.
  function showTip(target) {
    const data = target && tips[target.dataset.tip];
    if (!data) return (tip.hidden = true);
    tip.replaceChildren();
    const value = document.createElement('b');
    value.textContent = data.value;
    tip.append(value);
    for (const line of data.lines) {
      const span = document.createElement('span');
      span.textContent = line;
      tip.append(span);
    }
    tip.hidden = false;
    const box = el.getBoundingClientRect();
    const r = target.getBoundingClientRect();
    const mark = target.querySelector('.col-bar, .hbar-bar') ?? target;
    const m = mark.getBoundingClientRect();
    const x = Math.min(Math.max(8, (target.matches('.col') ? r.left + r.width / 2 : m.right) - box.left - tip.offsetWidth / 2), box.width - tip.offsetWidth - 8);
    const y = (target.matches('.col') ? m.top : r.top) - box.top - tip.offsetHeight - 8;
    tip.style.transform = `translate(${x}px, ${Math.max(4, y)}px)`;
  }
  el.addEventListener('pointerover', (e) => showTip(e.target.closest('[data-tip]')));
  el.addEventListener('pointerleave', () => (tip.hidden = true));
  el.addEventListener('focusin', (e) => showTip(e.target.closest('[data-tip]')));
  el.addEventListener('focusout', () => (tip.hidden = true));
  body.addEventListener('scroll', () => (tip.hidden = true));

  return { render };
}
