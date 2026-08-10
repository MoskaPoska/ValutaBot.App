namespace ValutaBot.MiniApp;

public static partial class MiniAppUI
{
    public static string GetJsScript()
    {
        return @"
        const tg = window.Telegram ? window.Telegram.WebApp : null;
        if (tg) {
            try {
                tg.expand();
                tg.ready();
            } catch(e) {}
        }

        function getCustomInitData() {
            const urlParams = new URLSearchParams(window.location.search);
            const userId = urlParams.get('custom_user_id') || urlParams.get('userId');
            const userSign = urlParams.get('custom_user_sign') || urlParams.get('userSign');
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
                otc: ['EUR/USD OTC']
            },
            commodities: {
                otc: []
            },
            crypto: {
                otc: []
            },
            stocks: {
                otc: []
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
            const majors = ['EUR/USD OTC'];
            return arr.map(function(a) {
                var star = top.indexOf(a) !== -1 ? '<span class="top-star">в…</span>' : '';
                var cls = majors.indexOf(a) !== -1 ? 'asset-item major' : 'asset-item';
                return '<div class="' + cls + '" data-asset="' + a + '">' + a + star + '</div>';
            }).join('');
        }

        function changeTopCategory(el) {
            if (!el) return;
            el = el.closest('.top-cat-btn') || el;
            document.querySelectorAll('.top-cat-btn').forEach(c => c.classList.remove('active'));
            el.classList.add('active');
            let cat = el.getAttribute('data-cat') || 'fiat';
            if (!assetsData[cat]) cat = 'fiat';
            const gridEl = document.getElementById('assetGrid');
            if (gridEl) {
                gridEl.innerHTML = `<div class='otc-scroll' style='grid-column:1/-1'><div class='asset-grid'>${renderAssets(assetsData[cat].otc)}</div></div>`;
            }
            let firstAssetEl = document.querySelector('.asset-item');
            if (firstAssetEl) setAsset(firstAssetEl);
        }

        function toggleMenu(m, b) {
            document.querySelectorAll('.asset-menu, .tf-menu').forEach(menu => { 
                if(menu.id !== m) menu.classList.remove('show'); 
            });
            const menuEl = document.getElementById(m);
            if (menuEl) menuEl.classList.toggle('show');
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
            if (!el) return;
            el = el.closest('.asset-item') || el;
            let a = el.getAttribute('data-asset');
            if (!a) return;
            currentAsset = a;
            const selEl = document.getElementById('selectedAsset');
            if (selEl) selEl.innerText = a;
            document.querySelectorAll('.asset-item').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            const menuEl = document.getElementById('assetMenu');
            if (menuEl) menuEl.classList.remove('show');
            const sphere = document.getElementById('mainSphere');
            if (sphere) sphere.classList.remove('buy-signal', 'put-signal', 'neutral-signal');
            initPriceWebSocket();
        }

        function setTf(el) {
            if (!el) return;
            el = el.closest('.tf-btn') || el;
            let tf = el.getAttribute('data-tf');
            if (!tf) return;
            currentTf = tf.toLowerCase();
            const selEl = document.getElementById('selectedTf');
            if (selEl) selEl.innerText = tf;
            document.querySelectorAll('.tf-btn').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            const menuEl = document.getElementById('tfMenu');
            if (menuEl) menuEl.classList.remove('show');
            const sphere = document.getElementById('mainSphere');
            if (sphere) sphere.classList.remove('buy-signal', 'put-signal', 'neutral-signal');
            initPriceWebSocket();
        }

        function handleGlobalInteraction(e) {
            const target = e.target;
            if (!target) return;

            const btnGet = target.closest('#btnGet');
            if (btnGet) {
                executeAnalysis();
                return;
            }

            const catBtn = target.closest('.top-cat-btn');
            if (catBtn) {
                changeTopCategory(catBtn);
                return;
            }

            const assetTrigger = target.closest('#assetBtn');
            if (assetTrigger) {
                toggleMenu('assetMenu', 'assetBtn');
                return;
            }

            const tfTrigger = target.closest('#tfBtn');
            if (tfTrigger) {
                toggleMenu('tfMenu', 'tfBtn');
                return;
            }

            const assetItem = target.closest('.asset-item');
            if (assetItem) {
                setAsset(assetItem);
                return;
            }

            const tfItem = target.closest('.tf-btn');
            if (tfItem) {
                setTf(tfItem);
                return;
            }

            const tabBtnChart = target.closest('#tabBtnChart');
            if (tabBtnChart) {
                switchResultTab('chart');
                return;
            }

            const tabBtnAI = target.closest('#tabBtnAI');
            if (tabBtnAI) {
                switchResultTab('ai');
                return;
            }

            if (!target.closest('.selector-section')) {
                document.querySelectorAll('.asset-menu, .tf-menu').forEach(m => m.classList.remove('show'));
            }
        }

        document.addEventListener('click', handleGlobalInteraction);

        (function() {
            var p = new URLSearchParams(window.location.search);
            var a = p.get('asset'), t = p.get('tf');
            if (a) {
                var el = document.querySelector('.asset-item[data-asset="' + a.toUpperCase() + '"]');
                if (el) { setAsset(el); el.scrollIntoView && el.scrollIntoView({ block: 'nearest' }); }
            }
            if (t) {
                var el = document.querySelector('.tf-btn[data-tf="' + t.toUpperCase() + '"]');
                if (el) setTf(el);
            }
        })();

        const topCatInitial = document.querySelector('.top-cat-btn');
        if (topCatInitial) changeTopCategory(topCatInitial);
        syncTime();
        initPriceWebSocket();
        
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
            const safeSetText = (id, txt) => { const el = document.getElementById(id); if (el) el.innerText = txt; };
            const safeSetHtml = (id, html) => { const el = document.getElementById(id); if (el) el.innerHTML = html; };
            const safeSetStyle = (id, prop, val) => { const el = document.getElementById(id); if (el) el.style[prop] = val; };

            safeSetText('resProb', '--%');
            safeSetStyle('resProb', 'color', 'var(--accent)');
            safeSetText('resDir', '--');
            safeSetStyle('resDir', 'color', 'var(--subtext)');
            safeSetText('resDur', '--');
            safeSetText('resRsi', '--');
            safeSetStyle('resRsi', 'color', 'var(--subtext)');
            safeSetText('resEma', '--');
            safeSetText('resVol', '--');
            safeSetStyle('resVol', 'color', 'var(--subtext)');
            safeSetHtml('probChart', '');
            safeSetHtml('dirChart', '<svg viewBox=\'0 0 80 40\'><path d=\'M10 35 L40 5 L70 35\' stroke=\'var(--dim)\' stroke-width=\'2.5\' fill=\'none\' stroke-linecap=\'round\' stroke-linejoin=\'round\' opacity=\'0.3\'/></svg>');
            safeSetHtml('durChart', '');
            safeSetStyle('resultsTabBar', 'display', 'none');
            safeSetStyle('resultsGrid', 'display', 'none');
            safeSetStyle('tabContentAI', 'display', 'none');
            safeSetStyle('mlEnsembleCard', 'display', 'none');
            safeSetStyle('confluenceCard', 'display', 'none');
            safeSetStyle('mcCard', 'display', 'none');
            safeSetStyle('reasoningCard', 'display', 'none');
            safeSetStyle('newsCard', 'display', 'none');
            safeSetStyle('welcomeSec', 'display', 'flex');
            safeSetStyle('topCategories', 'display', 'flex');
            document.querySelectorAll('.res-card').forEach(c => c.classList.remove('flash'));
        }

        function flashResults() {
            document.querySelectorAll('.res-card').forEach(c => {
                c.classList.remove('flash');
                void c.offsetWidth;
                c.classList.add('flash');
            });
        }
        
        function parseMd(text) {
            if (!text) return '';
            return text.replace(/\*\*(.*?)\*\*/g, '<b>$1</b>')
                       .replace(/\*(.*?)\*/g, '<i>$1</i>')
                       .replace(/\n/g, '<br/>');
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
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 20 L70 20' stroke='var(--dim)' stroke-width='2.5' stroke-dasharray='4 4' fill='none' stroke-linecap='round' opacity='0.5'/><circle cx='40' cy='20' r='3.5' fill='var(--dim)'/></svg>`;
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

            function escapeHtml(str) {
                if (!str) return '';
                return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
            }

            const safeTitle = escapeHtml(title);
            const safeDesc = escapeHtml(desc);
            const safeDebug = escapeHtml(debugText);

            errDisp.innerHTML = `
                <div class="error-header">${safeTitle}</div>
                <div class="error-desc">${safeDesc}</div>
                <div class="error-debug-toggle" onclick="toggleErrorDebug(this)">в–ё Р”РµС‚Р°Р»Рё РѕС‚Р»Р°РґРєРё</div>
                <div class="error-debug-content" id="errorDebugContent" style="display: none;">${safeDebug}</div>
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

        async function executeAnalysis() {
            const btn = document.getElementById('btnGet');
            if (btn && btn.disabled) return;
            const sphere = document.getElementById('mainSphere');
            
            try {
                const ed = document.getElementById('errorDisplay');
                if (ed) ed.style.display = 'none';
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
                    if (sphere) sphere.classList.remove('analyzing');
                    if (btn) {
                        btn.disabled = false;
                        btn.innerText = 'РџРћР›РЈР§РРўР¬ РђРќРђР›РР—';
                    }

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
                        if (rsiEl) {
                            rsiEl.innerText = data.rsi;
                            rsiEl.style.color = data.rsi > 70 ? '#ff1744' : data.rsi < 30 ? '#00e676' : 'var(--subtext)';
                        }
                    }
                    if (data.ema !== undefined) {
                        const emaEl = document.getElementById('resEma');
                        if (emaEl) emaEl.innerText = data.ema;
                    }
                    if (data.volumeStrength !== undefined) {
                        const volEl = document.getElementById('resVol');
                        if (volEl) {
                            const vs = data.volumeStrength;
                            if (Math.abs(vs) > 0.1) {
                                volEl.innerText = vs > 0 ? 'в†‘ ' + vs.toFixed(1) + 'x' : 'в†“ ' + Math.abs(vs).toFixed(1) + 'x';
                                volEl.style.color = vs > 0.5 ? '#00e676' : vs < -0.5 ? '#ff1744' : 'var(--subtext)';
                            } else {
                                volEl.innerText = 'Р‘Р°Р»Р°РЅСЃ';
                                volEl.style.color = 'var(--subtext)';
                            }
                        }
                    }
                    if (data.tfConflict) {
                        const rp = document.getElementById('resProb');
                        if (rp) rp.innerText += ' вљ пёЏ';
                    }

                    // в”Ђв”Ђ ML Ensemble Card в”Ђв”Ђ
                    if (data.llmReport && data.llmReport !== 'LLM-СЃРІРѕРґРєР° Р·Р°РіСЂСѓР¶Р°РµС‚СЃСЏ...') {
                        if (data.llmReport.includes('РћС„С„Р»Р°Р№РЅ РёР»Рё РЅРёР·РєР°СЏ СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ')) {
                            const mlCard = document.getElementById('mlEnsembleCard');
                            if (mlCard) mlCard.style.display = 'none';
                        } else {
                            const mlCard = document.getElementById('mlEnsembleCard');
                            if (mlCard) mlCard.style.display = 'block';
                            const badge = document.getElementById('mlEnsembleBadge');
                            const isEnabled = data.lgbmModelVersion && data.lgbmModelVersion !== 'disabled';
                            if (badge) {
                                badge.innerText = isEnabled ? 'рџ§  ML РђРЅСЃР°РјР±Р»СЊ' : 'вљ пёЏ ML';
                                badge.style.background = isEnabled ? 'linear-gradient(135deg,#8b5cf6,#6d28d9)' : 'rgba(100,100,100,0.4)';
                            }
                            const dir = document.getElementById('mlEnsembleDir');
                            if (dir && data.lgbmDirection) {
                                dir.innerText = data.lgbmDirection === 'BUY' ? 'Р’Р’Р•Р РҐ' : data.lgbmDirection === 'PUT' ? 'Р’РќРР—' : 'вЂ”';
                                dir.style.color = data.lgbmDirection === 'BUY' ? '#a78bfa' : data.lgbmDirection === 'PUT' ? '#f472b6' : 'var(--subtext)';
                            }
                            const rep = document.getElementById('mlEnsembleReport');
                            if (rep) {
                                rep.innerHTML = parseMd(data.llmReport);
                            }
                        }
                    }

                    // в”Ђв”Ђ Confluence + Win Rate Card в”Ђв”Ђ
                    const confCard = document.getElementById('confluenceCard');
                    if (confCard) confCard.style.display = 'block';
                    const confLabel = document.getElementById('confluenceLabel');
                    if (confLabel) confLabel.innerText = data.confluenceLabel || 'РђРЅР°Р»РёР·';
                    const goldenBadge = document.getElementById('goldenSetupBadge');
                    if (goldenBadge) goldenBadge.style.display = data.goldenSetup ? 'inline-block' : 'none';
                    const wrAssetEl = document.getElementById('winRateAsset');
                    if (wrAssetEl) {
                        if (data.winRateAsset != null) {
                            const pct = Math.round(data.winRateAsset * 100);
                            wrAssetEl.innerText = pct + '%';
                            wrAssetEl.style.color = pct >= 55 ? '#10b981' : pct >= 50 ? '#f59e0b' : '#f43f5e';
                        } else {
                            wrAssetEl.innerText = 'РЅРµС‚ РґР°РЅРЅС‹С…';
                            wrAssetEl.style.color = 'var(--subtext)';
                        }
                    }
                    const wrOverallEl = document.getElementById('winRateOverall');
                    if (wrOverallEl) {
                        if (data.winRateOverall != null) {
                            const pct = Math.round(data.winRateOverall * 100);
                            wrOverallEl.innerText = pct + '%';
                            wrOverallEl.style.color = pct >= 55 ? '#10b981' : pct >= 50 ? '#f59e0b' : '#f43f5e';
                        } else {
                            wrOverallEl.innerText = 'РЅРµС‚ РґР°РЅРЅС‹С…';
                        }
                    }
                    const sigCountEl = document.getElementById('signalsCount');
                    if (sigCountEl) {
                        const verified = data.signalsVerified || 0;
                        const pending = data.signalsPending || 0;
                        sigCountEl.innerText = verified + (pending > 0 ? ' (+' + pending + ')' : '');
                    }

                    // в”Ђв”Ђ Monte Carlo & Risk Card в”Ђв”Ђ
                    if (data.evLabel || data.kellyLabel) {
                        const mcCard = document.getElementById('mcCard');
                        if (mcCard) mcCard.style.display = 'block';
                        const mcSimEl = document.getElementById('mcSimCount');
                        if (mcSimEl && data.monteCarloIterations) {
                            mcSimEl.innerText = (data.monteCarloSuccess || 0) + ' / ' + data.monteCarloIterations + ' СѓРґР°С‡РЅС‹С…';
                        }
                        const evEl = document.getElementById('mcEv');
                        if (evEl) {
                            evEl.innerText = data.evLabel || '--';
                            evEl.style.color = (data.evPct && data.evPct > 0) ? '#10b981' : '#f43f5e';
                        }
                        const kellyEl = document.getElementById('mcKelly');
                        if (kellyEl) {
                            kellyEl.innerText = data.kellyLabel || '--';
                            kellyEl.style.color = (data.kellyRiskPct && data.kellyRiskPct > 0) ? '#f59e0b' : '#ff1744';
                        }
                        const wfEl = document.getElementById('wfStatus');
                        if (wfEl) {
                            if (data.wfIsCooloffActive) {
                                wfEl.innerText = 'РћС…Р»Р°Р¶РґРµРЅРёРµ';
                                wfEl.style.color = '#ff1744';
                            } else {
                                wfEl.innerText = 'Р’ РЅРѕСЂРјРµ';
                                wfEl.style.color = '#10b981';
                            }
                        }
                    }

                    // в”Ђв”Ђ Reasoning Card в”Ђв”Ђ
                    if (data.claudeReasoning) {
                        const rCard = document.getElementById('reasoningCard');
                        if (rCard) rCard.style.display = 'block';
                        const rText = document.getElementById('reasoningText');
                        if (rText) rText.innerText = data.claudeReasoning;
                        const rDir = document.getElementById('reasoningDir');
                        if (rDir) {
                            rDir.innerText = data.direction === 'BUY' ? 'Р’Р’Р•Р РҐ' : data.direction === 'PUT' ? 'Р’РќРР—' : 'РќР•Р™РўР РђР›Р¬РќРћ';
                            rDir.style.color = data.direction === 'BUY' ? '#a78bfa' : data.direction === 'PUT' ? '#f472b6' : 'var(--dim)';
                        }
                    }

                    // в”Ђв”Ђ News Card в”Ђв”Ђ
                    if (data.newsScore && Math.abs(data.newsScore) > 0.1 && data.newsSummary) {
                        const nCard = document.getElementById('newsCard');
                        if (nCard) nCard.style.display = 'block';
                        const nSent = document.getElementById('newsSentimentEl');
                        if (nSent) {
                            nSent.innerText = data.newsSentiment || '--';
                            nSent.style.color = data.newsScore > 0 ? '#00e676' : '#ff1744';
                        }
                        const nSum = document.getElementById('newsSummaryEl');
                        if (nSum) nSum.innerText = data.newsSummary;
                    }

                    const probBars = pricesToBars(data.chartData, 16);
                    if (probBars.length) renderMiniChart('probChart', probBars, '');

                    renderDirSvg(data.direction);

                    const durBars = pricesToBars(data.chartData, 8);
                    if (durBars.length) renderMiniChart('durChart', durBars, '');

                    const tabReg = document.getElementById('resultsTabBar');
                    if (tabReg) tabReg.style.display = 'flex';
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
        }
        ";
    }
}

