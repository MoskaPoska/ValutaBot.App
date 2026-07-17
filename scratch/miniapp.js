
        const tg = window.Telegram.WebApp;
        if(tg) tg.expand();

        function getCustomInitData() {
            const urlParams = new URLSearchParams(window.location.search);
            const userId = urlParams.get('userId');
            const userSign = urlParams.get('userSign');
            if (userId && userSign) {
                return `custom_user_id=${userId}&custom_user_sign=${userSign}`;
            }
            return '';
        }

        let currentAsset = 'EUR/USD OTC';
        let currentTf = 'm1';
        let syncStatusInterval = null;

        const assetsData = {
            fiat: {
                otc: ['EUR/USD OTC', 'GBP/USD OTC', 'AUD/USD OTC', 'USD/JPY OTC', 'EUR/JPY OTC', 'GBP/JPY OTC', 'USD/CAD OTC', 'USD/CHF OTC', 'NZD/USD OTC', 'EUR/GBP OTC', 'AUD/CAD OTC', 'CAD/CHF OTC', 'EUR/CHF OTC', 'EUR/NZD OTC', 'NZD/JPY OTC', 'USD/BRL OTC', 'USD/IDR OTC', 'USD/PKR OTC', 'USD/DZD OTC', 'NGN/USD OTC', 'LBP/USD OTC', 'TND/USD OTC', 'JOD/CNY OTC', 'OMR/CNY OTC', 'SAR/CNY OTC']
            },
            commodities: {
                otc: ['GOLD OTC', 'SILVER OTC', 'BRENT OTC', 'OIL OTC']
            },
            crypto: {
                otc: ['BTC/USDT OTC', 'ETH/USDT OTC', 'SOL/USDT OTC']
            },
            stocks: {
                otc: ['AAPL OTC', 'TSLA OTC', 'AMZN OTC', 'GOOGL OTC', 'MSFT OTC']
            }
        };

        function getTopAssets() {
            try {
                const h = JSON.parse(localStorage.getItem('vhistory') || '[]');
                var freq = {};
                for (var i = 0; i < h.length; i++) { var e = h[i]; freq[e.asset] = (freq[e.asset] || 0) + 1; }
                return Object.keys(freq).sort(function(a,b) { return freq[b] - freq[a]; }).slice(0, 8);
            } catch(e) { return []; }
        }

        function renderAssets(arr) {
            const top = getTopAssets();
            const majors = ['EUR/USD OTC', 'GBP/USD OTC', 'AUD/USD OTC'];
            return arr.map(function(a) {
                var star = top.indexOf(a) !== -1 ? '<span class=\x27top-star\x27>в…</span>' : '';
                var cls = majors.indexOf(a) !== -1 ? 'asset-item major' : 'asset-item';
                return '<div class=\x27' + cls + '\x27 data-asset=\x27' + a + '\x27 onclick=\x27setAsset(this)\x27>' + a + star + '</div>';
            }).join('');
        }

        function changeTopCategory(el) {
            document.querySelectorAll('.top-cat-btn').forEach(c => c.classList.remove('active'));
            el.classList.add('active');
            let cat = el.getAttribute('data-cat');
            document.getElementById('assetGrid').innerHTML = `<div class='otc-scroll' style='grid-column:1/-1'><div class='asset-grid'>${renderAssets(assetsData[cat].otc)}</div></div>`;
            let firstAssetEl = document.querySelector('.asset-item');
            if(firstAssetEl) setAsset(firstAssetEl);
        }

        function toggleMenu(m, b) {
            document.querySelectorAll('.asset-menu, .tf-menu').forEach(menu => { if(menu.id !== m) menu.classList.remove('show'); });
            document.getElementById(m).classList.toggle('show');
        }

        let priceSocket = null;
        let lastPriceVal = 0;

        function initPriceWebSocket() {
            closePriceWebSocket();

            const isSecondsTf = currentTf.startsWith('s');
            const livePriceContainer = document.getElementById('livePriceContainer');
            
            if (!isSecondsTf) {
                if (livePriceContainer) livePriceContainer.style.display = 'none';
                return;
            }

            if (livePriceContainer) livePriceContainer.style.display = 'flex';
            const valEl = document.getElementById('livePriceValue');
            if (valEl) {
                valEl.innerText = 'Р—РђР“Р РЈР—РљРђ...';
                valEl.className = 'live-price-value';
            }

            try {
                const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
                const wsUrl = `${protocol}//${window.location.host}/ws/prices?asset=${encodeURIComponent(currentAsset)}`;
                
                priceSocket = new WebSocket(wsUrl);

                priceSocket.onmessage = function(event) {
                    try {
                        const data = JSON.parse(event.data);
                        if (data && data.price !== undefined) {
                            const newPrice = data.price;
                            updateLivePriceUI(newPrice);
                        }
                    } catch (e) {
                        console.error('Error parsing WS message:', e);
                    }
                };

                priceSocket.onclose = function() {
                    console.log('Price WebSocket closed');
                };

                priceSocket.onerror = function(err) {
                    console.error('Price WebSocket error:', err);
                };
            } catch (err) {
                console.error('Failed to create WebSocket:', err);
            }
        }

        function closePriceWebSocket() {
            if (priceSocket) {
                try {
                    priceSocket.close();
                } catch(e) {}
                priceSocket = null;
            }
            lastPriceVal = 0;
        }

        function updateLivePriceUI(price) {
            const valEl = document.getElementById('livePriceValue');
            if (!valEl) return;

            const isHighVal = price > 100;
            const formatted = price.toFixed(isHighVal ? 2 : 5);

            valEl.innerText = formatted;

            if (lastPriceVal > 0) {
                if (price > lastPriceVal) {
                    valEl.className = 'live-price-value up';
                } else if (price < lastPriceVal) {
                    valEl.className = 'live-price-value down';
                }
                
                setTimeout(() => {
                    if (valEl.innerText === formatted) {
                        valEl.className = 'live-price-value';
                    }
                }, 400);
            } else {
                valEl.className = 'live-price-value';
            }

            lastPriceVal = price;
        }

        function setAsset(el) {
            let a = el.getAttribute('data-asset');
            currentAsset = a;
            document.getElementById('selectedAsset').innerText = a;
            document.querySelectorAll('.asset-item').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            document.getElementById('assetMenu').classList.remove('show');
            const sphere = document.getElementById('mainSphere');
            if (sphere) sphere.classList.remove('buy-signal', 'put-signal', 'neutral-signal');
            initPriceWebSocket();
            pollSyncStatus();
        }

        function setTf(el) {
            let tf = el.getAttribute('data-tf');
            currentTf = tf.toLowerCase();
            document.getElementById('selectedTf').innerText = tf;
            document.querySelectorAll('.tf-btn').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            document.getElementById('tfMenu').classList.remove('show');
            const sphere = document.getElementById('mainSphere');
            if (sphere) sphere.classList.remove('buy-signal', 'put-signal', 'neutral-signal');
            initPriceWebSocket();
        }

        document.addEventListener('click', function(e) {
            if (!e.target.closest('.selector-section')) {
                document.querySelectorAll('.asset-menu, .tf-menu').forEach(m => m.classList.remove('show'));
            }
        });

        (function() {
            var p = new URLSearchParams(window.location.search);
            var a = p.get('asset'), t = p.get('tf');
            if (a) {
                var el = document.querySelector('.asset-item[data-asset=""' + a.toUpperCase() + '""]');
                if (el) { setAsset(el); el.scrollIntoView && el.scrollIntoView({ block: 'nearest' }); }
            }
            if (t) {
                var el = document.querySelector('.tf-btn[data-tf=""' + t.toUpperCase() + '""]');
                if (el) setTf(el);
            }
        })();

        changeTopCategory(document.querySelector('.top-cat-btn'));
        syncTime();
        initPriceWebSocket();
        startSyncStatusPoller();
        
        var timeOffset = 0;

        async function syncTime() {
            try {
                var r = await fetch('/api/time', {
                    headers: {
                        'X-Telegram-Init-Data': tg ? tg.initData : ''
                    }
                });
                var d = await r.json();
                timeOffset = d.t - Date.now();
            } catch(e) { timeOffset = 0; }
        }

        function getTfSeconds() {
            const map = { s3:3, s5:5, s10:10, s15:15, s30:30, m1:60, m2:120, m3:180, m5:300, m15:900, m30:1800, h1:3600, h4:14400, d1:86400 };
            return map[currentTf] || 60;
        }

        function updateCountdown() {
            const tfSec = getTfSeconds();
            const now = Math.floor((Date.now() + timeOffset) / 1000);
            const remaining = tfSec - (now % tfSec);
            const mins = Math.floor(remaining / 60);
            const secs = remaining % 60;
            const el = document.getElementById('candleTime');
            if (!el) return;
            el.innerText = `${mins}:${secs.toString().padStart(2,'0')}`;
            el.className = 'time' + (remaining <= 5 ? ' critical' : remaining <= 15 ? ' warning' : '');
        }

        function switchResultTab(tabName) {
            const btnChart = document.getElementById('tabBtnChart');
            const btnAI = document.getElementById('tabBtnAI');
            const contentChart = document.getElementById('resultsGrid');
            const contentAI = document.getElementById('tabContentAI');

            if (tabName === 'chart') {
                btnChart.classList.add('active');
                btnAI.classList.remove('active');
                if (contentChart) contentChart.style.display = 'grid';
                contentAI.style.display = 'none';
            } else {
                btnChart.classList.remove('active');
                btnAI.classList.add('active');
                if (contentChart) contentChart.style.display = 'none';
                contentAI.style.display = 'block';
            }
        }

        function clearResults() {
            document.getElementById('resProb').innerText = '--%';
            document.getElementById('resProb').style.color = 'var(--accent)';
            document.getElementById('resDir').innerText = '--';
            document.getElementById('resDir').style.color = 'var(--subtext)';
            document.getElementById('resDur').innerText = '--';
            document.getElementById('resRsi').innerText = '--';
            document.getElementById('resRsi').style.color = 'var(--subtext)';
            document.getElementById('resEma').innerText = '--';
            document.getElementById('resVol').innerText = '--';
            document.getElementById('resVol').style.color = 'var(--subtext)';
            document.getElementById('probChart').innerHTML = '';
            document.getElementById('dirChart').innerHTML = '<svg viewBox=\'0 0 80 40\'><path d=\'M10 35 L40 5 L70 35\' stroke=\'var(--dim)\' stroke-width=\'2.5\' fill=\'none\' stroke-linecap=\'round\' stroke-linejoin=\'round\' opacity=\'0.3\'/></svg>';
            document.getElementById('durChart').innerHTML = '';
            if (document.getElementById('resultsTabBar')) document.getElementById('resultsTabBar').style.display = 'none';
            if (document.getElementById('resultsGrid')) document.getElementById('resultsGrid').style.display = 'none';
            document.getElementById('tabContentAI').style.display = 'none';
            document.getElementById('levelsBar').style.display = 'none';
            document.getElementById('mlCard').style.display = 'none';
            document.getElementById('claudeCard').style.display = 'none';
            document.getElementById('newsCard').style.display = 'none';
            document.getElementById('welcomeSec').style.display = 'flex';
            document.getElementById('topCategories').style.display = 'flex';
            document.querySelectorAll('.res-card').forEach(c => c.classList.remove('flash'));
        }

        function flashResults() {
            document.querySelectorAll('.res-card').forEach(c => {
                c.classList.remove('flash');
                void c.offsetWidth;
                c.classList.add('flash');
            });
        }

        setInterval(updateCountdown, 1000);
        setTimeout(updateCountdown, 100);

        function renderMiniChart(containerId, values, color) {
            const container = document.getElementById(containerId);
            if(!container) return;
            const max = Math.max(...values, 1);
            container.innerHTML = values.map(v => {
                const h = Math.max(4, (v / max) * 38);
                return `<div class='res-chart-bar ${color}' style='height:${h}px'></div>`;
            }).join('');
        }

        function renderDirSvg(direction) {
            const chart = document.getElementById('dirChart');
            if(!chart) return;
            if(direction === 'BUY') {
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 35 L30 25 L45 30 L70 5' stroke='#00e676' stroke-width='3' fill='none' stroke-linecap='round' stroke-linejoin='round'/><circle cx='70' cy='5' r='3.5' fill='#00e676'/></svg>`;
            } else if(direction === 'PUT') {
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 5 L30 15 L45 10 L70 35' stroke='#ff1744' stroke-width='3' fill='none' stroke-linecap='round' stroke-linejoin='round'/><circle cx='70' cy='35' r='3.5' fill='#ff1744'/></svg>`;
            } else {
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 35 L40 5 L70 35' stroke='var(--dim)' stroke-width='2.5' fill='none' stroke-linecap='round' stroke-linejoin='round' opacity='0.3'/></svg>`;
            }
        }

        /* в”Ђв”Ђв”Ђ Status bar animation (non-blocking) в”Ђв”Ђв”Ђ */
        const sbStatuses = ['Р—РђР“Р РЈР—РљРђ Р”РђРќРќР«РҐ', 'РџРћР›РЈР§Р•РќРР• Р¦Р•РќР«', 'РђРќРђР›РР— Р Р«РќРљРђ'];
        let sbTimer = null, sbIdx = 0;

        function startStatusBar() {
            const sb = document.getElementById('statusBar');
            if (!sb) return;
            sb.classList.add('show');
            const title = document.getElementById('sbTitle');
            const sub = document.getElementById('sbSub');
            if (title) title.innerHTML = 'РђРќРђР›РР—РР РЈР® Р Р«РќРћРљ<span class=\'blink\'>.</span>';
            if (sub) { sub.textContent = sbStatuses[0]; sub.className = 'sb-sub'; }
            sbIdx = 0;

            if (sbTimer) clearInterval(sbTimer);
            sbTimer = setInterval(() => {
                const title = document.getElementById('sbTitle');
                if (title) {
                    const m = title.textContent.match(/\.+$/);
                    const dots = m ? m[0].length : 0;
                    title.innerHTML = 'РђРќРђР›РР—РР РЈР® Р Р«РќРћРљ<span class=\'blink\'>' + '.'.repeat((dots % 3) + 1) + '</span>';
                }
                sbIdx = (sbIdx + 1) % sbStatuses.length;
                const sub = document.getElementById('sbSub');
                if (sub) {
                    sub.classList.add('fade');
                    setTimeout(() => { sub.textContent = sbStatuses[sbIdx]; sub.classList.remove('fade'); }, 200);
                }
            }, 900);
        }

        function stopStatusBar() {
            const sb = document.getElementById('statusBar');
            if (sb) sb.classList.remove('show');
            if (sbTimer) { clearInterval(sbTimer); sbTimer = null; }
        }

        function pricesToBars(prices, count) {
            if (!prices || !prices.length) return [];
            const tail = prices.slice(-count);
            const min = Math.min.apply(null, tail);
            const max = Math.max.apply(null, tail);
            const span = max - min;
            if (span < 1e-12) return tail.map(() => 0.5);
            return tail.map(p => 0.05 + 0.9 * (p - min) / span);
        }

        function renderPriceChart(canvasId, prices, direction) {
            const c = document.getElementById(canvasId);
            if (!c || !prices || prices.length < 2) return;
            const dpr = window.devicePixelRatio || 1;
            const cssW = c.clientWidth || c.parentNode.clientWidth || 320;
            const cssH = c.clientHeight || 140;
            c.width = Math.round(cssW * dpr);
            c.height = Math.round(cssH * dpr);
            const ctx = c.getContext('2d');
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, cssW, cssH);

            const pad = { l: 8, r: 8, t: 10, b: 10 };
            const w = cssW - pad.l - pad.r;
            const h = cssH - pad.t - pad.b;
            const min = Math.min.apply(null, prices);
            const max = Math.max.apply(null, prices);
            const span = Math.max(max - min, 1e-12);
            const x = i => pad.l + (i / (prices.length - 1)) * w;
            const y = v => pad.t + h - ((v - min) / span) * h;

            ctx.strokeStyle = 'rgba(124,77,255,0.08)';
            ctx.lineWidth = 1;
            for (let k = 1; k < 4; k++) {
                const gy = pad.t + (h / 4) * k;
                ctx.beginPath(); ctx.moveTo(pad.l, gy); ctx.lineTo(pad.l + w, gy); ctx.stroke();
            }

            const stroke = direction === 'BUY' ? '#00e676'
                          : direction === 'PUT' ? '#ff1744'
                          : '#7c4dff';
            const fillTop = direction === 'BUY' ? 'rgba(0,230,118,0.35)'
                          : direction === 'PUT' ? 'rgba(255,23,68,0.35)'
                          : 'rgba(124,77,255,0.35)';

            const grad = ctx.createLinearGradient(0, pad.t, 0, pad.t + h);
            grad.addColorStop(0, fillTop);
            grad.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.beginPath();
            ctx.moveTo(x(0), pad.t + h);
            for (let i = 0; i < prices.length; i++) ctx.lineTo(x(i), y(prices[i]));
            ctx.lineTo(x(prices.length - 1), pad.t + h);
            ctx.closePath();
            ctx.fillStyle = grad;
            ctx.fill();

            ctx.beginPath();
            ctx.moveTo(x(0), y(prices[0]));
            for (let i = 1; i < prices.length; i++) ctx.lineTo(x(i), y(prices[i]));
            ctx.strokeStyle = stroke;
            ctx.lineWidth = 2;
            ctx.shadowColor = stroke;
            ctx.shadowBlur = 8;
            ctx.stroke();
            ctx.shadowBlur = 0;

            const lx = x(prices.length - 1), ly = y(prices[prices.length - 1]);
            ctx.beginPath();
            ctx.arc(lx, ly, 3.5, 0, Math.PI * 2);
            ctx.fillStyle = stroke;
            ctx.fill();

            const lastTxt = prices[prices.length - 1].toFixed(prices[0] > 100 ? 2 : 5);
            ctx.font = '600 11px Inter, sans-serif';
            ctx.fillStyle = stroke;
            const tw = ctx.measureText(lastTxt).width;
            ctx.fillRect(lx - tw - 10, ly - 9, tw + 8, 16);
            ctx.fillStyle = '#0b0a1f';
            ctx.fillText(lastTxt, lx - tw - 6, ly + 3);
        }

        function renderError(rawError, debugText) {
            const errDisp = document.getElementById('errorDisplay');
            if (!errDisp) return;

            let title = 'вљ пёЏ РћС€РёР±РєР°';
            let desc = 'РџСЂРѕРёР·РѕС€Р»Р° РЅРµРїСЂРµРґРІРёРґРµРЅРЅР°СЏ РѕС€РёР±РєР° РїСЂРё РѕР±СЂР°Р±РѕС‚РєРµ Р·Р°РїСЂРѕСЃР°.';

            if (rawError) {
                const errLower = rawError.toLowerCase();
                
                if (errLower.includes('run out of api credits') || errLower.includes('api credits') || (errLower.includes('limit') && errLower.includes('twelvedata'))) {
                    title = 'вљ пёЏ Р›РёРјРёС‚ TwelveData РёСЃС‡РµСЂРїР°РЅ';
                    desc = 'РџСЂРµРІС‹С€РµРЅ СЃСѓС‚РѕС‡РЅС‹Р№ Р»РёРјРёС‚ Р·Р°РїСЂРѕСЃРѕРІ Рє API TwelveData (800 С€С‚). РџРѕР¶Р°Р»СѓР№СЃС‚Р°, РїРѕРґРѕР¶РґРёС‚Рµ РѕР±РЅРѕРІР»РµРЅРёСЏ Р»РёРјРёС‚Р° (СЃР»РµРґСѓСЋС‰РёР№ РґРµРЅСЊ).';
                } else if (errLower.includes('too many requests') || errLower.includes('rate limit') || errLower.includes('429')) {
                    title = 'вљ пёЏ РџСЂРµРІС‹С€РµРЅ Р»РёРјРёС‚ Р·Р°РїСЂРѕСЃРѕРІ';
                    const match = rawError.match(/(\d+)s/);
                    const sec = match ? ` РЅР° ${match[1]} СЃРµРє.` : '';
                    desc = `РЎР»РёС€РєРѕРј РјРЅРѕРіРѕ Р·Р°РїСЂРѕСЃРѕРІ. РџРѕР¶Р°Р»СѓР№СЃС‚Р°, РїРѕРґРѕР¶РґРёС‚Рµ${sec} РїРµСЂРµРґ СЃР»РµРґСѓСЋС‰РёРј СЃРєР°РЅРёСЂРѕРІР°РЅРёРµРј.`;
                } else if (errLower.includes('access denied') || errLower.includes('deposit required')) {
                    title = 'вљ пёЏ Р”РѕСЃС‚СѓРї РѕРіСЂР°РЅРёС‡РµРЅ';
                    desc = 'Р”Р»СЏ РёСЃРїРѕР»СЊР·РѕРІР°РЅРёСЏ Р±РѕС‚Р° РЅРµРѕР±С…РѕРґРёРјР° СЂРµРіРёСЃС‚СЂР°С†РёСЏ РЅР° Pocket Option Рё РІРЅРµСЃРµРЅРёРµ РґРµРїРѕР·РёС‚Р°.';
                } else if (errLower.includes('signature') || errLower.includes('initdata') || errLower.includes('unauthorized') || errLower.includes('401')) {
                    title = 'вљ пёЏ РћС€РёР±РєР° Р°РІС‚РѕСЂРёР·Р°С†РёРё';
                    desc = 'РџРѕР¶Р°Р»СѓР№СЃС‚Р°, РїРµСЂРµР·Р°РїСѓСЃС‚РёС‚Рµ Р±РѕС‚Р° С‡РµСЂРµР· Telegram, С‡С‚РѕР±С‹ РѕР±РЅРѕРІРёС‚СЊ СЃРµСЃСЃРёСЋ.';
                } else if (errLower.includes('asset and timeframe')) {
                    title = 'вљ пёЏ РќРµРІРµСЂРЅС‹Рµ РїР°СЂР°РјРµС‚СЂС‹';
                    desc = 'РќРµРѕР±С…РѕРґРёРјРѕ РІС‹Р±СЂР°С‚СЊ РІР°Р»СЋС‚РЅСѓСЋ РїР°СЂСѓ Рё С‚Р°Р№РјС„СЂРµР№Рј.';
                } else if (errLower.includes('pocketid')) {
                    title = 'вљ пёЏ РћС€РёР±РєР° РїСЂРѕС„РёР»СЏ';
                    desc = 'РќРµ СѓРєР°Р·Р°РЅ Pocket Option ID.';
                } else if (errLower.includes('api key') || errLower.includes('apikey')) {
                    title = 'вљ пёЏ РЎР±РѕР№ РєРѕРЅС„РёРіСѓСЂР°С†РёРё';
                    desc = 'РќР° СЃРµСЂРІРµСЂРµ РЅРµ РЅР°СЃС‚СЂРѕРµРЅ API-РєР»СЋС‡ TwelveData.';
                } else if (errLower.includes('plan') || errLower.includes('subscription') || errLower.includes('tier')) {
                    title = 'вљ пёЏ РћРіСЂР°РЅРёС‡РµРЅРёРµ С‚Р°СЂРёС„Р°';
                    desc = 'Р’Р°С€ С‚Р°СЂРёС„ TwelveData РЅРµ РїРѕРґРґРµСЂР¶РёРІР°РµС‚ СЌС‚РѕС‚ Р°РєС‚РёРІ РёР»Рё С‚Р°Р№РјС„СЂРµР№Рј. РџРѕРїСЂРѕР±СѓР№С‚Рµ РІС‹Р±СЂР°С‚СЊ РґСЂСѓРіРѕР№ РёРЅСЃС‚СЂСѓРјРµРЅС‚.';
                } else if (errLower.includes('fetch') || errLower.includes('network') || errLower.includes('failed') || errLower.includes('connect')) {
                    title = 'вљ пёЏ РћС€РёР±РєР° СЃРѕРµРґРёРЅРµРЅРёСЏ';
                    desc = 'РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕРґРєР»СЋС‡РёС‚СЊСЃСЏ Рє СЃРµСЂРІРµСЂСѓ. РџРѕР¶Р°Р»СѓР№СЃС‚Р°, РїСЂРѕРІРµСЂСЊС‚Рµ РёРЅС‚РµСЂРЅРµС‚-СЃРѕРµРґРёРЅРµРЅРёРµ.';
                } else {
                    title = 'вљ пёЏ РЎР±РѕР№ РѕРїРµСЂР°С†РёРё';
                    desc = rawError;
                    desc = desc.replace(/failed/gi, 'РѕС€РёР±РєР°');
                    desc = desc.replace(/error/gi, 'СЃР±РѕР№');
                    desc = desc.replace(/internal server error/gi, 'Р’РЅСѓС‚СЂРµРЅРЅСЏСЏ РѕС€РёР±РєР° СЃРµСЂРІРµСЂР°');
                }
            }

            errDisp.innerHTML = `
                <div class=""error-header"">${title}</div>
                <div class=""error-desc"">${desc}</div>
                <div class=""error-debug-toggle"" onclick=""toggleErrorDebug(this)"">в–ё Р”РµС‚Р°Р»Рё РѕС‚Р»Р°РґРєРё</div>
                <div class=""error-debug-content"" id=""errorDebugContent"" style=""display: none;"">${debugText}</div>
            `;
            errDisp.style.display = 'block';
        }

        function toggleErrorDebug(btn) {
            const content = document.getElementById('errorDebugContent');
            if (!content) return;
            const isHidden = content.style.display === 'none';
            content.style.display = isHidden ? 'block' : 'none';
            btn.innerText = isHidden ? 'в–ѕ РЎРєСЂС‹С‚СЊ РґРµС‚Р°Р»Рё' : 'в–ё Р”РµС‚Р°Р»Рё РѕС‚Р»Р°РґРєРё';
        }

        document.getElementById('btnGet').onclick = async () => {
            const btn = document.getElementById('btnGet');
            const sphere = document.getElementById('mainSphere');
            
            try {
                document.getElementById('errorDisplay').style.display = 'none';
                clearResults();
                startStatusBar();

                requestAnimationFrame(() => {
                    if (sphere) {
                        sphere.classList.remove('buy-signal', 'put-signal', 'neutral-signal');
                        sphere.classList.add('analyzing');
                    }
                    if (btn) {
                        btn.disabled = true;
                        btn.innerText = 'РЎРљРђРќРР РћР’РђРќРР•...';
                    }
                });

                const startTime = Date.now();

                const res = await fetch(`/api/analyze?asset=${encodeURIComponent(currentAsset)}&timeframe=${currentTf}&_=${Date.now()}`, {
                    headers: {
                        'X-Telegram-Init-Data': tg && tg.initData ? tg.initData : getCustomInitData()
                    }
                });
                const data = await res.json();

                const elapsed = Date.now() - startTime;
                const remainingDelay = Math.max(0, 2000 - elapsed);

                setTimeout(() => {
                    stopStatusBar();
                    sphere.classList.remove('analyzing');
                    btn.disabled = false;
                    btn.innerText = 'РџРћР›РЈР§РРўР¬ РђРќРђР›РР—';

                    if(data.error) {
                        const debugMsg = `вЂў Р”Р»РёРЅР° С‚РѕРєРµРЅР°: ${tg && tg.initData ? tg.initData.length : 0}\nвЂў РџР»Р°С‚С„РѕСЂРјР°: ${tg ? tg.platform : 'unknown'}\nвЂў РђРґСЂРµСЃ: ${window.location.href}`;
                        renderError(data.error, debugMsg);
                        return;
                    }



                    const isUnclear = data.unclear === true;
                    const resDir = document.getElementById('resDir');
                    if (data.direction === 'BUY') {
                        resDir.innerText = 'Р’Р’Р•Р РҐ';
                        resDir.style.color = '#00e676';
                        sphere.classList.add('buy-signal');
                    } else if (data.direction === 'PUT') {
                        resDir.innerText = 'Р’РќРР—';
                        resDir.style.color = '#ff1744';
                        sphere.classList.add('put-signal');
                    } else {
                        resDir.innerText = 'РќР•Р™РўР РђР›Р¬РќРћ';
                        resDir.style.color = 'var(--dim)';
                        sphere.classList.add('neutral-signal');
                    }

                    document.getElementById('resProb').innerText = data.probability + '%';
                    document.getElementById('resProb').style.color = data.probability >= 90 ? '#00e676' : data.probability >= 85 ? '#ffd600' : 'var(--accent)';

                    document.getElementById('resDur').innerText = data.duration;

                    if (data.rsi !== undefined) {
                        const rsiEl = document.getElementById('resRsi');
                        rsiEl.innerText = data.rsi;
                        rsiEl.style.color = data.rsi > 70 ? '#ff1744' : data.rsi < 30 ? '#00e676' : 'var(--subtext)';
                    }
                    if (data.ema !== undefined) {
                        document.getElementById('resEma').innerText = data.ema;
                    }
                    if (data.volumeStrength !== undefined) {
                        const volEl = document.getElementById('resVol');
                        const vs = data.volumeStrength;
                        volEl.innerText = vs > 0 ? '\u2B06 ' + vs.toFixed(1) : vs < 0 ? '\u2B07 ' + Math.abs(vs).toFixed(1) : '\u2014';
                        volEl.style.color = vs > 0.5 ? '#00e676' : vs < -0.5 ? '#ff1744' : 'var(--subtext)';
                    }
                    if (data.tfConflict) {
                        document.getElementById('resProb').innerText += ' \u26A0\uFE0F';
                    }
                    // ML and News cards are disabled/hidden by user request.
                    /*
                    if (data.mlDirection && data.mlDirection !== 'NEUTRAL') {
                        const mc = document.getElementById('mlCard');
                        mc.style.display = 'flex';
                        const dirEl = document.getElementById('mlDir');
                        dirEl.innerText = data.mlDirection === 'BUY' ? '\u2191 Р’Р’Р•Р РҐ' : '\u2193 Р’РќРР—';
                        dirEl.style.color = data.mlDirection === 'BUY' ? '#00e676' : '#ff1744';
                        document.getElementById('mlConf').innerText = data.mlConfidence + '%';
                    }

                    if (data.newsScore !== undefined) {
                        const nc = document.getElementById('newsCard');
                        nc.style.display = 'block';
                        const senEl = document.getElementById('newsSentiment');
                        senEl.innerText = data.newsSentiment;
                        if (data.newsScore > 0.5) { senEl.style.color = '#00e676'; }
                        else if (data.newsScore < -0.5) { senEl.style.color = '#ff1744'; }
                        else { senEl.style.color = 'var(--subtext)'; }
                        document.getElementById('newsSummary').innerText = data.newsSummary;
                        const nl = document.getElementById('newsList');
                        if (data.newsHeadlines && data.newsHeadlines.length) {
                            nl.innerHTML = data.newsHeadlines.map(h => `<div class='news-list-item'>${h}</div>`).join('');
                        } else {
                            nl.innerHTML = '';
                        }
                    }
                    */

                    if (data.claudeDirection && data.claudeReasoning) {
                        const cc = document.getElementById('claudeCard');
                        cc.style.display = 'block';
                        const badge = document.getElementById('aiModelBadge');
                        badge.innerText = data.aiModel ? 'рџ§  ' + data.aiModel : 'рџ§  AI РЅРµРґРѕСЃС‚СѓРїРµРЅ';
                        const senEl = document.getElementById('claudeSentiment');
                        senEl.innerText = data.claudeDirection === 'BUY' ? 'Р’Р’Р•Р РҐ' : data.claudeDirection === 'PUT' ? 'Р’РќРР—' : 'вЂ”';
                        senEl.style.color = data.claudeDirection === 'BUY' ? '#a78bfa' : data.claudeDirection === 'PUT' ? '#f472b6' : 'var(--subtext)';
                        let reasoningText = data.claudeReasoning;
                        if (data.claudeProbability && data.claudeDirection !== 'NEUTRAL') {
                            reasoningText += ` (РІРµСЂРѕСЏС‚РЅРѕСЃС‚СЊ: ${data.claudeProbability}%)`;
                        }
                        document.getElementById('claudeReasoning').innerText = reasoningText;
                    }

                    const probBars = pricesToBars(data.chartData, 16);
                    if (probBars.length) renderMiniChart('probChart', probBars, '');

                    renderDirSvg(data.direction);

                    const durBars = pricesToBars(data.chartData, 8);
                    if (durBars.length) renderMiniChart('durChart', durBars, '');

                    // renderPriceChart('priceChart', data.chartData || [], data.direction);

                    if(data.levels) {
                        const L = data.levels;
                        const renderLevel = (id, lv) => {
                            document.getElementById(id).className = `result ${lv.direction.toLowerCase()}`;
                            document.getElementById(id).innerText = `${lv.direction === 'NEUTRAL' ? '\u2014' : lv.direction}`;
                            document.getElementById(id.replace('res', '')).style.display = 'flex';
                        };
                        renderLevel('ll1res', L.level1);
                        renderLevel('ll2res', L.level2);
                        renderLevel('ll3res', L.level3);
                        document.getElementById('ltotalVotes').innerHTML = `<span style='color:var(--green)'>\u2191 ${data.levels.level1.buy + data.levels.level2.buy + data.levels.level3.buy}</span> / <span style='color:var(--red)'>\u2193 ${data.levels.level1.put + data.levels.level2.put + data.levels.level3.put}</span>`;
                        const td = document.getElementById('ltotalDir');
                        td.className = `dir ${data.direction.toLowerCase()}`;
                        td.innerText = data.direction === 'BUY' ? '\u2191 Р’Р’Р•Р РҐ' : '\u2193 Р’РќРР—';
                        document.getElementById('levelsBar').style.display = 'block';
                    }

                    document.getElementById('resultsTabBar').style.display = 'flex';
                    switchResultTab('chart');
                    flashResults();

                }, remainingDelay);
            } catch(e) {
                stopStatusBar();
                sphere.classList.remove('analyzing');
                btn.disabled = false;
                btn.innerText = 'РџРћР›РЈР§РРўР¬ РђРќРђР›РР—';
                const catchMsg = `вЂў Р”Р»РёРЅР° С‚РѕРєРµРЅР°: ${tg && tg.initData ? tg.initData.length : 0}\nвЂў РџР»Р°С‚С„РѕСЂРјР°: ${tg ? tg.platform : 'unknown'}\nвЂў РђРґСЂРµСЃ: ${window.location.href}`;
                renderError(e.message, catchMsg);
            }
        };

        /* в”Ђв”Ђв”Ђ News toggle в”Ђв”Ђв”Ђ */
        function toggleNews() {
            const list = document.getElementById('newsList');
            const toggle = document.getElementById('newsToggle');
            const open = list.classList.toggle('open');
            toggle.innerText = open ? '\u25BD Р—Р°РіРѕР»РѕРІРєРё' : '\u25B8 Р—Р°РіРѕР»РѕРІРєРё';
        }

        /* в”Ђв”Ђв”Ђ Sync Modal Functions в”Ђв”Ђв”Ђ */
        function openSyncModal() {
            const modal = document.getElementById('syncModal');
            const txt = document.getElementById('userscriptText');
            if (modal && txt) {
                txt.value = generateUserscript();
                modal.style.display = 'flex';
                // Trigger reflow for transition
                modal.offsetHeight;
                modal.style.opacity = '1';
            }
        }

        function closeSyncModal() {
            const modal = document.getElementById('syncModal');
            if (modal) {
                modal.style.opacity = '0';
                setTimeout(() => { modal.style.display = 'none'; }, 300);
            }
        }

        function copyUserscript() {
            const txt = document.getElementById('userscriptText');
            if (!txt) return;
            txt.select();
            txt.setSelectionRange(0, 99999);
            navigator.clipboard.writeText(txt.value).then(() => {
                const btn = document.getElementById('btnCopyScript');
                if (btn) {
                    const old = btn.innerText;
                    btn.innerText = 'РЎРєРѕРїРёСЂРѕРІР°РЅРѕ!';
                    btn.style.background = 'rgba(0, 230, 118, 0.2)';
                    btn.style.borderColor = '#00e676';
                    setTimeout(() => {
                        btn.innerText = old;
                        btn.style.background = 'rgba(124,77,255,0.15)';
                        btn.style.borderColor = 'var(--accent)';
                    }, 2000);
                }
            });
        }

        function generateUserscript() {
            const host = window.location.origin;
            return `// ==UserScript==
// @name         Pocket Option Live price sync for ValutaBot
// @namespace    http://tampermonkey.net/
// @version      1.2
// @description  Streams real-time Pocket Option OTC ticks to ValutaBot server
// @author       TradeBE
// @match        *://pocketoption.com/*
// @match        *://*.pocketoption.com/*
// @match        *://po.cash/*
// @match        *://*.po.cash/*
// @match        *://po.trade/*
// @match        *://*.po.trade/*
// @match        *://po.zone/*
// @match        *://*.po.zone/*
// @match        *://po2.cash/*
// @match        *://*.po2.cash/*
// @match        *://po3.cash/*
// @match        *://*.po3.cash/*
// @match        *://po4.cash/*
// @match        *://*.po4.cash/*
// @match        *://po5.cash/*
// @match        *://*.po5.cash/*
// @match        *://po6.cash/*
// @match        *://*.po6.cash/*
// @match        *://po7.cash/*
// @match        *://*.po7.cash/*
// @match        *://po8.cash/*
// @match        *://*.po8.cash/*
// @match        *://po9.cash/*
// @match        *://*.po9.cash/*
// @match        *://po10.cash/*
// @match        *://*.po10.cash/*
// @match        *://pocketoption.co/*
// @match        *://*.pocketoption.co/*
// @match        *://pocket-option.co/*
// @match        *://*.pocket-option.co/*
// @match        *://po-ru.co/*
// @match        *://*.po-ru.co/*
// @match        *://pocketoption-ru.co/*
// @match        *://*.pocketoption-ru.co/*
// @match        *://po.market/*
// @match        *://*.po.market/*
// @allFrames    true
// @connect      *
// @grant        GM_xmlhttpRequest
// @run-at       document-start
// ==/UserScript==

(function() {
    'use strict';
    console.log('[ValutaBot Sync] Userscript active. Intercepting WebSockets...');

    const targetWindow = (typeof unsafeWindow !== 'undefined') ? unsafeWindow : window;
    const BACKEND_URL = '"https://example.com"';

    function hookWebSocket(win) {
        if (!win || win.WebSocket._hooked) return;
        
        try {
            const RealWebSocket = win.WebSocket;
            win.WebSocket = function(url, protocols) {
                console.log('[ValutaBot Sync] Intercepted WebSocket connection:', url);
                const ws = new RealWebSocket(url, protocols);
                
                let msgCount = 0;
                let lastBinaryEvent = null;
                ws.addEventListener('message', function(event) {
                    try {
                        const data = event.data;
                        if (typeof data === 'string') {
                            if (msgCount < 15) {
                                msgCount++;
                                console.log('[ValutaBot Sync] Msg #' + msgCount + ' (String):', data.substring(0, 150));
                            }
                            if (data.startsWith('45')) {
                                try {
                                    const firstBrace = data.indexOf('[');
                                    if (firstBrace !== -1) {
                                        const parsed = JSON.parse(data.substring(firstBrace));
                                        if (Array.isArray(parsed) && parsed.length >= 1) {
                                            lastBinaryEvent = parsed[0];
                                        }
                                    }
                                } catch (e) {}
                            } else if (data.startsWith('42')) {
                                const parsed = JSON.parse(data.substring(2));
                                if (Array.isArray(parsed) && parsed.length >= 2) {
                                    const eventName = parsed[0];
                                    const payload = parsed[1];
                                    
                                    if (eventName === 'updateStream') {
                                        processUpdateStream(payload);
                                    }
                                }
                            }
                        } else {
                            // Binary message (ArrayBuffer)
                            let arrayBuffer = null;
                            if (data instanceof ArrayBuffer) {
                                arrayBuffer = data;
                            } else if (data && data.constructor && data.constructor.name === 'ArrayBuffer') {
                                arrayBuffer = data;
                            }
                            
                            if (arrayBuffer) {
                                try {
                                    const decoder = new TextDecoder('utf-8');
                                    const text = decoder.decode(arrayBuffer);
                                    if (msgCount < 15) {
                                        msgCount++;
                                        console.log('[ValutaBot Sync] Msg #' + msgCount + ' (Decoded binary for ' + lastBinaryEvent + '):', text.substring(0, 200));
                                    }
                                    
                                    try {
                                        const parsed = JSON.parse(text);
                                        if (lastBinaryEvent === 'updateCharts') {
                                            processUpdateCharts(parsed);
                                        } else if (lastBinaryEvent === 'updateStream') {
                                            processUpdateStream(parsed);
                                        }
                                    } catch (jsonErr) {}
                                } catch (decErr) {}
                            } else {
                                if (msgCount < 15) {
                                    msgCount++;
                                    console.log('[ValutaBot Sync] Msg #' + msgCount + ' (Binary type):', data.constructor ? data.constructor.name : typeof data);
                                }
                            }
                        }
                    } catch (e) {
                        // Ignore parse/format errors
                    }
                });
                
                return ws;
            };
            win.WebSocket.prototype = RealWebSocket.prototype;
            win.WebSocket._hooked = true;
        } catch (e) {
            console.error('[ValutaBot Sync] Failed to hook WebSocket in window:', e);
        }
    }

    // Hook main window
    hookWebSocket(targetWindow);

    // Hook dynamically created iframes
    try {
        const originalCreateElement = targetWindow.document.createElement;
        targetWindow.document.createElement = function(tagName, options) {
            const el = originalCreateElement.call(targetWindow.document, tagName, options);
            if (el && tagName.toLowerCase() === 'iframe') {
                try {
                    el.addEventListener('load', function() {
                        try {
                            if (el.contentWindow) {
                                hookWebSocket(el.contentWindow);
                            }
                        } catch (e) {}
                    });
                    setTimeout(() => {
                        try {
                            if (el.contentWindow) {
                                hookWebSocket(el.contentWindow);
                            }
                        } catch (e) {}
                    }, 0);
                } catch (e) {}
            }
            return el;
        };
    } catch (e) {
        console.error('[ValutaBot Sync] Failed to hook document.createElement:', e);
    }

    // Hook contentWindow getter on HTMLIFrameElement prototype
    try {
        const proto = targetWindow.HTMLIFrameElement.prototype;
        const desc = Object.getOwnPropertyDescriptor(proto, 'contentWindow');
        if (desc && desc.get) {
            Object.defineProperty(proto, 'contentWindow', {
                get: function() {
                    const win = desc.get.call(this);
                    if (win) {
                        try {
                            hookWebSocket(win);
                        } catch (e) {}
                    }
                    return win;
                },
                configurable: true,
                enumerable: true
            });
        }
    } catch (e) {
        console.error('[ValutaBot Sync] Failed to hook HTMLIFrameElement.contentWindow:', e);
    }

    function processUpdateStream(payload) {
        if (Array.isArray(payload)) {
            for (const item of payload) {
                if (Array.isArray(item)) {
                    // [asset, timestamp, price] or [asset, price, timestamp]
                    let price = parseFloat(item[1]);
                    if (!isNaN(price) && price > 1000000) {
                        price = parseFloat(item[2]);
                    }
                    if (!isNaN(price)) {
                        sendPrice(item[0], price);
                    }
                } else if (item && typeof item === 'object') {
                    const asset = item.asset || item.symbol || item.id || item.ticker;
                    const price = parseFloat(item.price || item.value || item.close);
                    if (asset && !isNaN(price)) {
                        sendPrice(asset, price);
                    }
                }
            }
        } else if (payload && typeof payload === 'object') {
            const asset = payload.asset || payload.symbol || payload.id || payload.ticker;
            const price = parseFloat(payload.price || payload.value || payload.close);
            if (asset && !isNaN(price)) {
                sendPrice(asset, price);
            }
        }
    }

    function processUpdateCharts(payload) {
        if (Array.isArray(payload)) {
            for (const item of payload) {
                if (item && item.symbol && Array.isArray(item.data)) {
                    const symbol = item.symbol;
                    const history = item.data.map(c => ({
                        price: parseFloat(c[4]),
                        timestamp: parseInt(c[0])
                    })).filter(h => !isNaN(h.price) && !isNaN(h.timestamp));
                    
                    if (history.length > 0) {
                        sendHistory(symbol, history);
                    }
                }
            }
        }
    }

    function sendHistory(asset, history) {
        const normalized = normalize(asset);
        const endpoint = BACKEND_URL + '/api/update-otc-history';
        const payload = JSON.stringify({ asset: normalized, history: history });
        
        if (typeof GM_xmlhttpRequest !== 'undefined') {
            GM_xmlhttpRequest({
                method: 'POST',
                url: endpoint,
                data: payload,
                headers: {
                    'Content-Type': 'application/json'
                },
                onload: function(response) {
                    console.log('[ValutaBot Sync] History synced for ' + normalized + ' (' + history.length + ' points)');
                },
                onerror: function(err) {
                    console.error('[ValutaBot Sync] History sync failed for ' + normalized + ':', err);
                }
            });
        } else {
            fetch(endpoint, {
                method: 'POST',
                body: payload,
                headers: {
                    'Content-Type': 'application/json'
                }
            })
            .then(() => console.log('[ValutaBot Sync] History synced via fetch for ' + normalized))
            .catch(err => console.error('[ValutaBot Sync] Fetch history failed:', err));
        }
    }

    let lastSent = {};
    function sendPrice(asset, price) {
        const normalized = normalize(asset);
        
        // Limit updates to 10 per second per asset to save bandwidth
        const now = Date.now();
        if (lastSent[normalized] && (now - lastSent[normalized]) < 100) {
            return;
        }
        lastSent[normalized] = now;

        const endpoint = BACKEND_URL + '/api/update-otc-price?asset=' + encodeURIComponent(normalized) + '&price=' + price;
        if (typeof GM_xmlhttpRequest !== 'undefined') {
            GM_xmlhttpRequest({
                method: 'POST',
                url: endpoint,
                onload: function(response) {
                    console.log('[ValutaBot Sync] Price sent. Status: ' + response.status + ' Response: ' + response.responseText + ' for ' + normalized + ': ' + price);
                },
                onerror: function(err) {
                    console.error('[ValutaBot Sync] Failed to send price for ' + normalized + ':', err);
                }
            });
        } else {
            fetch(endpoint, { method: 'POST', mode: 'no-cors' })
                .then(() => {
                    console.log('[ValutaBot Sync] Price sent via fetch for ' + normalized + ': ' + price);
                })
                .catch(err => {
                    console.error('[ValutaBot Sync] Fetch failed for ' + normalized + ':', err);
                });
        }
    }

    function normalize(name) {
        let clean = name.toUpperCase().replace('_', ' ').replace('-', ' ').trim();
        if (clean.includes('OTC') && !clean.endsWith(' OTC')) {
            clean = clean.replace('OTC', '').trim() + ' OTC';
        }
        if (clean.length >= 6) {
            const first3 = clean.substring(0, 3);
            const next3 = clean.substring(3, 6);
            const remaining = clean.substring(6);
            if (/^[A-Z]{3}$/.test(first3) && /^[A-Z]{3}$/.test(next3)) {
                clean = first3 + '/' + next3 + remaining;
            }
        }
        return clean;
    }
})();`;
        }
        
        function startSyncStatusPoller() {
            if (syncStatusInterval) clearInterval(syncStatusInterval);
            pollSyncStatus();
            syncStatusInterval = setInterval(pollSyncStatus, 3000);
        }
        
        async function pollSyncStatus() {
            try {
                const res = await fetch(`/api/otc-status?asset=${encodeURIComponent(currentAsset)}`);
                const data = await res.json();
                
                const dot = document.getElementById('syncStatusDot');
                const txt = document.getElementById('syncStatusText');
                if (!dot || !txt) return;

                if (data.active) {
                    dot.classList.add('online');
                    txt.innerText = `РЎРёРЅС…СЂРѕРЅРёР·Р°С†РёСЏ: РђРєС‚РёРІРЅР° (${data.lastPrice > 100 ? data.lastPrice.toFixed(2) : data.lastPrice.toFixed(5)})`;
                    txt.style.color = '#00e676';
                } else {
                    dot.classList.remove('online');
                    txt.innerText = 'РЎРёРЅС…СЂРѕРЅРёР·Р°С†РёСЏ: РћР¶РёРґР°РЅРёРµ СЃРєСЂРёРїС‚Р°';
                    txt.style.color = 'var(--dim)';
                }
            } catch (e) {
                // Ignore background errors
            }
        }

    
