namespace ValutaBot.MiniApp;

public static partial class MiniAppUI
{
    public static string GetJsScript()
    {
        return @"
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
                var star = top.indexOf(a) !== -1 ? '<span class=\x27top-star\x27>★</span>' : '';
                var cls = majors.indexOf(a) !== -1 ? 'asset-item major' : 'asset-item';
                return '<div class=\x27' + cls + '\x27 data-asset=\x27' + a + '\x27 onclick=\x27setAsset(this)\x27>' + a + star + '</div>';
            }).join('');
        }

        function changeTopCategory(el) {
            document.querySelectorAll('.top-cat-btn').forEach(c => c.classList.remove('active'));
            el.classList.add('active');
            let cat = el.getAttribute('data-cat');
            document.getElementById('assetGrid').innerHTML = `<div class='otc-scroll' style='grid-column:1/-1'><div class='asset-grid'>${renderAssets(assetsData[cat].otc)}</div></div>`;
        }

        document.getElementById('assetGrid').innerHTML = `<div class='otc-scroll' style='grid-column:1/-1'><div class='asset-grid'>${renderAssets(assetsData.fiat.otc)}</div></div>`;

        function toggleMenu(menuId, btnId) {
            const m = document.getElementById(menuId);
            const b = document.getElementById(btnId);
            const isAss = menuId === 'assetMenu';
            const o = isAss ? document.getElementById('tfMenu') : document.getElementById('assetMenu');
            const ob = isAss ? document.getElementById('tfBtn') : document.getElementById('assetBtn');
            o.classList.remove('open'); ob.classList.remove('active');
            m.classList.toggle('open'); b.classList.toggle('active');
        }

        function setAsset(el) {
            currentAsset = el.getAttribute('data-asset');
            document.getElementById('selectedAsset').innerText = currentAsset;
            toggleMenu('assetMenu', 'assetBtn');
        }

        function setTf(el) {
            document.querySelectorAll('.tf-btn').forEach(b => b.classList.remove('active'));
            el.classList.add('active');
            currentTf = el.getAttribute('data-tf');
            document.getElementById('selectedTf').innerText = currentTf.toUpperCase();
            toggleMenu('tfMenu', 'tfBtn');
        }

        function switchResultTab(tabName) {
            const btnChart = document.getElementById('tabBtnChart');
            const btnAI = document.getElementById('tabBtnAI');
            const contentAI = document.getElementById('tabContentAI');

            if (tabName === 'chart') {
                if(btnChart) btnChart.classList.add('active');
                if(btnAI) btnAI.classList.remove('active');
                if(contentAI) contentAI.style.display = 'none';
            } else {
                if(btnAI) btnAI.classList.add('active');
                if(btnChart) btnChart.classList.remove('active');
                if(contentAI) contentAI.style.display = 'block';
            }
        }

        function flashResults() {
            document.querySelectorAll('.res-card').forEach(c => {
                c.classList.remove('flash');
                void c.offsetWidth;
                c.classList.add('flash');
            });
        }

        function toggleErrorDebug() {
            const box = document.getElementById('errorDebugBox');
            if (box) {
                box.style.display = box.style.display === 'none' ? 'block' : 'none';
            }
        }

        function renderError(userMsg, debugDetails) {
            const errBanner = document.getElementById('errorBanner');
            const errTitle = document.getElementById('errTitle');
            const errDesc = document.getElementById('errDesc');
            const debugBox = document.getElementById('errorDebugBox');

            if (errTitle) errTitle.innerText = '⚠️ Сервер временно недоступен';
            if (errDesc) errDesc.innerText = userMsg || 'Проверьте соединение с сетью и повторите попытку через минуту.';
            if (debugBox) {
                debugBox.innerText = debugDetails || 'Подробности недоступны.';
                debugBox.style.display = 'none';
            }
            if (errBanner) errBanner.style.display = 'block';
        }

        async function analyzeMarket() {
            const btn = document.getElementById('btnAnalyze');
            const sphere = document.getElementById('sphereContainer');
            const sDir = document.getElementById('sphereDir');
            const sProb = document.getElementById('sphereProb');
            const sReason = document.getElementById('sphereReason');
            const errBanner = document.getElementById('errorBanner');
            const resultsSec = document.getElementById('resultsSection');
            const progressSec = document.getElementById('progressSection');
            const barFill = document.getElementById('progressBarFill');
            const progressPct = document.getElementById('progressPct');

            btn.disabled = true;
            btn.innerText = 'СКАННИРОВАНИЕ...';
            sphere.classList.add('analyzing');
            if (errBanner) errBanner.style.display = 'none';
            if (resultsSec) resultsSec.style.display = 'none';
            if (progressSec) progressSec.style.display = 'block';

            sDir.innerText = '--';
            sProb.innerText = '--%';
            sReason.innerText = 'АНАЛИЗ РЫНКА...';

            let stepIndex = 0;
            const startTime = Date.now();

            function updateStep(idx, pct) {
                if (barFill) barFill.style.width = pct + '%';
                if (progressPct) progressPct.innerText = pct + '%';
                const st = document.getElementById('pstep' + idx);
                if (st) st.classList.add('done');
            }

            const stepTimer = setInterval(() => {
                stepIndex++;
                if (stepIndex === 1) updateStep(1, 25);
                else if (stepIndex === 2) updateStep(2, 50);
                else if (stepIndex === 3) updateStep(3, 75);
                else if (stepIndex === 4) updateStep(4, 90);
            }, 300);

            try {
                const initDataStr = tg && tg.initData ? tg.initData : getCustomInitData();
                const cleanAsset = encodeURIComponent(currentAsset);
                const cleanTf = encodeURIComponent(currentTf);
                const response = await fetch(`/api/analyze?asset=${cleanAsset}&timeframe=${cleanTf}`, {
                    headers: { 'X-Telegram-Init-Data': initDataStr }
                });

                clearInterval(stepTimer);
                updateStep(4, 100);

                if (!response.ok) {
                    const errJson = await response.json().catch(() => ({}));
                    throw new Error(errJson.message || errJson.error || `HTTP ${response.status}`);
                }

                const data = await response.json();
                const elapsed = Date.now() - startTime;
                const remainingDelay = Math.max(0, 1200 - elapsed);

                setTimeout(() => {
                    if (progressSec) progressSec.style.display = 'none';
                    sphere.classList.remove('analyzing');
                    btn.disabled = false;
                    btn.innerText = 'ПОЛУЧИТЬ АНАЛИЗ';

                    if (data.error) {
                        renderError(data.message || data.error, `Response error: ${JSON.stringify(data)}`);
                        return;
                    }

                    sphere.classList.remove('buy-signal', 'put-signal');
                    if (data.direction === 'BUY') {
                        sphere.classList.add('buy-signal');
                        sDir.innerText = 'ВВЕРХ ↑';
                    } else if (data.direction === 'PUT') {
                        sphere.classList.add('put-signal');
                        sDir.innerText = 'ВНИЗ ↓';
                    } else {
                        sDir.innerText = 'НЕЙТРАЛЬНО';
                    }

                    sProb.innerText = data.probability + '%';
                    sReason.innerText = data.adaptiveReasoning || data.reasoning || 'Анализ завершен.';

                    if (resultsSec) resultsSec.style.display = 'block';

                    const durText = document.getElementById('resDurationText');
                    if (durText) durText.innerText = data.duration || '1 МИНУТА';

                    const resRsi = document.getElementById('resRsi');
                    if (resRsi) resRsi.innerText = data.rsi ? data.rsi : '--';

                    const resEma = document.getElementById('resEma');
                    if (resEma) resEma.innerText = data.ema ? data.ema : '--';

                    const resVol = document.getElementById('resVol');
                    if (resVol) resVol.innerText = data.volumeStrength ? data.volumeStrength + 'x' : '--';

                    if (data.levels) {
                        const renderLevel = (id, obj) => {
                            const el = document.getElementById(id);
                            if (!el || !obj) return;
                            el.className = `result ${obj.dir.toLowerCase()}`;
                            el.innerText = obj.dir === 'BUY' ? '↑ ВВЕРХ' : obj.dir === 'PUT' ? '↓ ВНИЗ' : '— НЕЙТРАЛЬНО';
                            const lineEl = el.closest('.level-line');
                            if (lineEl) {
                                lineEl.style.display = 'flex';
                            }
                        };
                        renderLevel('ll1res', data.levels.level1);
                        renderLevel('ll2res', data.levels.level2);
                        renderLevel('ll3res', data.levels.level3);
                        const tvEl = document.getElementById('ltotalVotes');
                        if (tvEl) tvEl.innerHTML = `<span style='color:var(--green)'>\u2191 ${data.levels.level1.buy + data.levels.level2.buy + data.levels.level3.buy}</span> / <span style='color:var(--red)'>\u2193 ${data.levels.level1.put + data.levels.level2.put + data.levels.level3.put}</span>`;
                        const td = document.getElementById('ltotalDir');
                        if (td) {
                            td.className = `dir ${data.direction.toLowerCase()}`;
                            td.innerText = data.direction === 'BUY' ? '\u2191 ВВЕРХ' : data.direction === 'PUT' ? '\u2193 ВНИЗ' : '\u2014 НЕЙТРАЛЬНО';
                        }
                        const lbEl = document.getElementById('levelsBar');
                        if (lbEl) lbEl.style.display = 'block';
                    }

                    const tabReg = document.getElementById('resultsTabBar');
                    if (tabReg) tabReg.style.display = 'flex';
                    switchResultTab('chart');
                    flashResults();

                }, remainingDelay);
            } catch(e) {
                if (progressSec) progressSec.style.display = 'none';
                sphere.classList.remove('analyzing');
                btn.disabled = false;
                btn.innerText = 'ПОЛУЧИТЬ АНАЛИЗ';
                const catchMsg = `• Длина токена: ${tg && tg.initData ? tg.initData.length : 0}\n• Платформа: ${tg ? tg.platform : 'unknown'}\n• Адрес: ${window.location.href}`;
                renderError(e.message, catchMsg);
            }
        }
";
    }
}
