/* ── ComparaJá · Favorites ── */

const FAV_KEY = 'comparaja_favorites_v1';

function getFavs() {
  try { return JSON.parse(localStorage.getItem(FAV_KEY) || '{}'); }
  catch { return {}; }
}

function saveFavs(favs) {
  localStorage.setItem(FAV_KEY, JSON.stringify(favs));
}

function isFav(id) {
  return id in getFavs();
}

function toggleFav(id, data) {
  const favs = getFavs();
  if (id in favs) { delete favs[id]; }
  else             { favs[id] = data; }
  saveFavs(favs);
  return id in favs;
}

function getFavCount() {
  return Object.keys(getFavs()).length;
}

// ── Counter badge ──────────────────────────────────────────────────────────

function syncCounters() {
  const count = getFavCount();
  document.querySelectorAll('.fav-counter').forEach(el => {
    el.textContent = count;
    el.style.display = count > 0 ? 'flex' : 'none';
  });
}

// ── Heart buttons on product cards ────────────────────────────────────────

function applyHeart(btn, active) {
  if (active) {
    btn.innerHTML = '♥';
    btn.classList.add('is-fav');
    btn.title = 'Remover dos favoritos';
  } else {
    btn.innerHTML = '♡';
    btn.classList.remove('is-fav');
    btn.title = 'Adicionar aos favoritos';
  }
}

function initCardHearts() {
  document.querySelectorAll('.product-card[data-product-id] .card-fav').forEach(btn => {
    const card = btn.closest('.product-card');
    const id   = card.dataset.productId;
    if (!id) return;

    applyHeart(btn, isFav(id));

    btn.addEventListener('click', e => {
      e.preventDefault();
      e.stopPropagation();

      const data = {
        id:       id,
        name:     card.dataset.productName     || '',
        price:    card.dataset.productPrice    || '',
        site:     card.dataset.productSite     || '',
        stores:   card.dataset.productStores   || '',
        image:    card.dataset.productImage    || '',
        category: card.dataset.productCategory || '',
      };

      const nowFav = toggleFav(id, data);
      applyHeart(btn, nowFav);

      btn.classList.add('fav-pop');
      btn.addEventListener('animationend', () => btn.classList.remove('fav-pop'), { once: true });

      syncCounters();
    });
  });
}

// ── Detail page heart button ───────────────────────────────────────────────

function initDetailHeart() {
  const btn = document.querySelector('.detail-fav-btn');
  if (!btn) return;

  const id = btn.dataset.productId;
  if (!id) return;

  applyDetailHeart(btn, isFav(id));

  btn.addEventListener('click', () => {
    const data = {
      id:     id,
      name:   btn.dataset.productName  || '',
      price:  btn.dataset.productPrice || '',
      site:   btn.dataset.productSite  || '',
      stores: btn.dataset.productStores || '',
      image:  btn.dataset.productImage || '',
    };

    const nowFav = toggleFav(id, data);
    applyDetailHeart(btn, nowFav);

    btn.classList.add('fav-pop');
    btn.addEventListener('animationend', () => btn.classList.remove('fav-pop'), { once: true });

    syncCounters();
  });
}

function applyDetailHeart(btn, active) {
  if (active) {
    btn.innerHTML = '<span class="detail-fav-icon">♥</span><span>Favoritado</span>';
    btn.classList.add('is-fav');
    btn.title = 'Remover dos favoritos';
  } else {
    btn.innerHTML = '<span class="detail-fav-icon">♡</span><span>Favoritar</span>';
    btn.classList.remove('is-fav');
    btn.title = 'Adicionar aos favoritos';
  }
}

// ── Favorites page ─────────────────────────────────────────────────────────

function esc(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function fmtPrice(raw) {
  const n = parseFloat(raw);
  if (isNaN(n)) return null;
  return n.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function renderFavoritesPage() {
  const grid     = document.getElementById('favorites-grid');
  const empty    = document.getElementById('favorites-empty');
  const countEl  = document.getElementById('fav-page-count');
  if (!grid) return;

  const favs  = getFavs();
  const items = Object.values(favs);

  if (countEl) {
    countEl.textContent = items.length === 0
      ? ''
      : `${items.length} produto${items.length > 1 ? 's' : ''} salvo${items.length > 1 ? 's' : ''}`;
  }

  if (items.length === 0) {
    grid.style.display  = 'none';
    if (empty) empty.style.display = 'flex';
    return;
  }

  if (empty) empty.style.display = 'none';
  grid.style.display = 'grid';
  grid.innerHTML = '';

  items.forEach((p, idx) => {
    const priceFormatted = fmtPrice(p.price);
    const priceHtml = priceFormatted
      ? `<p class="card-price">R$ ${priceFormatted}</p>`
      : `<p class="card-price card-price--na">Preço indisponível</p>`;

    const metaHtml = p.site
      ? `<div class="card-meta">
           <span class="card-store">${esc(p.site)}</span>
           <span class="card-stores-count">· ${esc(p.stores)} loja(s)</span>
         </div>`
      : '';

    const imgHtml = p.image
      ? `<img src="${esc(p.image)}" alt="${esc(p.name)}" loading="lazy" />`
      : `<div class="img-placeholder">📦</div>`;

    const card = document.createElement('a');
    card.href      = `/public/product/${esc(p.id)}`;
    card.className = 'product-card fav-card';
    card.style.cssText = 'opacity:0;transform:translateY(12px);transition:opacity .3s ease,transform .3s ease';
    card.dataset.productId = p.id;

    card.innerHTML = `
      <button class="card-fav is-fav" type="button" title="Remover dos favoritos">♥</button>
      <div class="card-img">${imgHtml}</div>
      <div class="card-body">
        <h2 class="card-title">${esc(p.name)}</h2>
        ${priceHtml}
        ${metaHtml}
      </div>
    `;

    // Remove-from-favorites button
    const favBtn = card.querySelector('.card-fav');
    favBtn.addEventListener('click', e => {
      e.preventDefault();
      e.stopPropagation();

      toggleFav(p.id, p);

      card.style.transition = 'opacity .25s ease, transform .25s ease';
      card.style.opacity    = '0';
      card.style.transform  = 'scale(0.88)';

      setTimeout(() => {
        card.remove();
        syncCounters();
        const remaining = grid.querySelectorAll('.fav-card').length;
        if (countEl) {
          countEl.textContent = remaining === 0
            ? ''
            : `${remaining} produto${remaining > 1 ? 's' : ''} salvo${remaining > 1 ? 's' : ''}`;
        }
        if (remaining === 0) {
          grid.style.display  = 'none';
          if (empty) empty.style.display = 'flex';
        }
      }, 260);
    });

    grid.appendChild(card);

    // Staggered entrance animation
    setTimeout(() => {
      card.style.opacity   = '1';
      card.style.transform = 'translateY(0)';
    }, 60 + idx * 55);
  });
}

// ── Init ───────────────────────────────────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
  syncCounters();
  initCardHearts();
  initDetailHeart();
  renderFavoritesPage();
});
