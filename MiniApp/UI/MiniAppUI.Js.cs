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
                valEl.innerText = 'ЗАГРУЗКА...';
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
                var el = document.querySelector('.asset-item[data-asset=""""' + a.toUpperCase() + '""""]');
                if (el) { setAsset(el); el.scrollIntoView && el.scrollIntoView({ block: 'nearest' }); }
            }
            if (t) {
                var el = document.querySelector('.tf-btn[data-tf=""""' + t.toUpperCase() + '""""]');
                if (el) setTf(el);
            }
        })();

        changeTopCategory(document.querySelector('.top-cat-btn'));
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
            safeSetStyle('levelsBar', 'display', 'none');
            safeSetStyle('mlCard', 'display', 'none');
            safeSetStyle('claudeCard', 'display', 'none');
            safeSetStyle('lgbmCard', 'display', 'none');
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

        /* ─── Status bar animation (non-blocking) ─── */
        const sbStatuses = ['ЗАГРУЗКА ДАННЫХ', 'ПОЛУЧЕНИЕ ЦЕНЫ', 'АНАЛИЗ РЫНКА'];
        let sbTimer = null, sbIdx = 0;

        function startStatusBar() {
            const sb = document.getElementById('statusBar');
            if (!sb) return;
            sb.classList.add('show');
            const title = document.getElementById('sbTitle');
            const sub = document.getElementById('sbSub');
            if (title) title.innerHTML = 'АНАЛИЗИРУЮ РЫНОК<span class=\'blink\'>.</span>';
            if (sub) { sub.textContent = sbStatuses[0]; sub.className = 'sb-sub'; }
            sbIdx = 0;

            if (sbTimer) clearInterval(sbTimer);
            sbTimer = setInterval(() => {
                const title = document.getElementById('sbTitle');
                if (title) {
                    const m = title.textContent.match(/\.+$/);
                    const dots = m ? m[0].length : 0;
                    title.innerHTML = 'АНАЛИЗИРУЮ РЫНОК<span class=\'blink\'>' + '.'.repeat((dots % 3) + 1) + '</span>';
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

            let title = '⚠️ Ошибка';
            let desc = 'Произошла непредвиденная ошибка при обработке запроса.';

            if (rawError) {
                const errLower = rawError.toLowerCase();
                
                if (errLower.includes('run out of api credits') || errLower.includes('api credits') || (errLower.includes('limit') && errLower.includes('twelvedata'))) {
                    title = '⚠️ Лимит TwelveData исчерпан';
                    desc = 'Превышен суточный лимит запросов к API TwelveData (800 шт). Пожалуйста, подождите обновления лимита (следующий день).';
                } else if (errLower.includes('too many requests') || errLower.includes('rate limit') || errLower.includes('429')) {
                    title = '⚠️ Превышен лимит запросов';
                    const match = rawError.match(/(\d+)s/);
                    const sec = match ? ` на ${match[1]} сек.` : '';
                    desc = `Слишком много запросов. Пожалуйста, подождите${sec} перед следующим сканированием.`;
                } else if (errLower.includes('access denied') || errLower.includes('deposit required')) {
                    title = '⚠️ Доступ ограничен';
                    desc = 'Для использования бота необходима регистрация на Pocket Option и внесение депозита.';
                } else if (errLower.includes('signature') || errLower.includes('initdata') || errLower.includes('unauthorized') || errLower.includes('401')) {
                    title = '⚠️ Ошибка авторизации';
                    desc = 'Пожалуйста, перезапустите бота через Telegram, чтобы обновить сессию.';
                } else if (errLower.includes('asset and timeframe')) {
                    title = '⚠️ Неверные параметры';
                    desc = 'Необходимо выбрать валютную пару и таймфрейм.';
                } else if (errLower.includes('pocketid')) {
                    title = '⚠️ Ошибка профиля';
                    desc = 'Не указан Pocket Option ID.';
                } else if (errLower.includes('api key') || errLower.includes('apikey')) {
                    title = '⚠️ Сбой конфигурации';
                    desc = 'На сервере не настроен API-ключ TwelveData.';
                } else if (errLower.includes('plan') || errLower.includes('subscription') || errLower.includes('tier')) {
                    title = '⚠️ Ограничение тарифа';
                    desc = 'Ваш тариф TwelveData не поддерживает этот актив или таймфрейм. Попробуйте выбрать другой инструмент.';
                } else if (errLower.includes('fetch') || errLower.includes('network') || errLower.includes('failed') || errLower.includes('connect')) {
                    title = '⚠️ Ошибка соединения';
                    desc = 'Не удалось подключиться к серверу. Пожалуйста, проверьте интернет-соединение.';
                } else {
                    title = '⚠️ Сбой операции';
                    desc = rawError;
                    desc = desc.replace(/failed/gi, 'ошибка');
                    desc = desc.replace(/error/gi, 'сбой');
                    desc = desc.replace(/internal server error/gi, 'Внутренняя ошибка сервера');
                }
            }

            function escapeHtml(str) {
                if (!str) return '';
                return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/""""/g, '&quot;').replace(/'/g, '&#039;');
            }

            const safeTitle = escapeHtml(title);
            const safeDesc = escapeHtml(desc);
            const safeDebug = escapeHtml(debugText);

            errDisp.innerHTML = `
                <div class=""""error-header"""">${safeTitle}</div>
                <div class=""""error-desc"""">${safeDesc}</div>
                <div class=""""error-debug-toggle"""" onclick=""""toggleErrorDebug(this)"""">▸ Детали отладки</div>
                <div class=""""error-debug-content"""" id=""""errorDebugContent"""" style=""""display: none;"""">${safeDebug}</div>
            `;
            errDisp.style.display = 'block';
        }

        function toggleErrorDebug(btn) {
            const content = document.getElementById('errorDebugContent');
            if (!content) return;
            const isHidden = content.style.display === 'none';
            content.style.display = isHidden ? 'block' : 'none';
            btn.innerText = isHidden ? '▾ Скрыть детали' : '▸ Детали отладки';
        }

        document.getElementById('btnGet').onclick = async () => {
            const btn = document.getElementById('btnGet');
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
                        btn.innerText = 'СКАНИРОВАНИЕ...';
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
                        btn.innerText = 'ПОЛУЧИТЬ АНАЛИЗ';
                    }

                    if(data.error) {
                        const debugMsg = `• Длина токена: ${tg && tg.initData ? tg.initData.length : 0}\n• Платформа: ${tg ? tg.platform : 'unknown'}\n• Адрес: ${window.location.href}`;
                        renderError(data.error, debugMsg);
                        return;
                    }



                    const isUnclear = data.unclear === true;
                    const resDir = document.getElementById('resDir');
                    if (data.direction === 'BUY') {
                        resDir.innerText = 'ВВЕРХ';
                        resDir.style.color = '#00e676';
                        sphere.classList.add('buy-signal');
                    } else if (data.direction === 'PUT') {
                        resDir.innerText = 'ВНИЗ';
                        resDir.style.color = '#ff1744';
                        sphere.classList.add('put-signal');
                    } else {
                        resDir.innerText = 'НЕЙТРАЛЬНО';
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
                                volEl.innerText = vs > 0 ? '↑ ' + vs.toFixed(1) + 'x' : '↓ ' + Math.abs(vs).toFixed(1) + 'x';
                                volEl.style.color = vs > 0.5 ? '#00e676' : vs < -0.5 ? '#ff1744' : 'var(--subtext)';
                            } else {
                                volEl.innerText = 'Баланс';
                                volEl.style.color = 'var(--subtext)';
                            }
                        }
                    }
                    if (data.tfConflict) {
                        const rp = document.getElementById('resProb');
                        if (rp) rp.innerText += ' \u26A0\uFE0F';
                    }

                    if (data.claudeDirection && data.claudeReasoning) {
                        const cc = document.getElementById('claudeCard');
                        if (cc) cc.style.display = 'block';
                        const badge = document.getElementById('aiModelBadge');
                        if (badge) badge.innerText = data.aiModel ? '🧠 ' + data.aiModel : '🧠 AI недоступен';
                        const senEl = document.getElementById('claudeSentiment');
                        if (senEl) {
                            senEl.innerText = data.claudeDirection === 'BUY' ? 'ВВЕРХ' : data.claudeDirection === 'PUT' ? 'ВНИЗ' : '—';
                            senEl.style.color = data.claudeDirection === 'BUY' ? '#a78bfa' : data.claudeDirection === 'PUT' ? '#f472b6' : 'var(--subtext)';
                        }
                        let reasoningText = data.claudeReasoning;
                        if (data.claudeProbability && data.claudeDirection !== 'NEUTRAL') {
                            reasoningText += ` (вероятность: ${data.claudeProbability}%)`;
                        }
                        const crEl = document.getElementById('claudeReasoning');
                        if (crEl) crEl.innerText = reasoningText;
                    }

                    // ── LightGBM card ──
                    if (data.lgbmDirection && data.lgbmDirection !== 'NEUTRAL' && data.lgbmConfidence) {
                        const lc = document.getElementById('lgbmCard');
                        if (lc) lc.style.display = 'flex';
                        const ldirEl = document.getElementById('lgbmDir');
                        if (ldirEl) {
                            ldirEl.innerText = data.lgbmDirection === 'BUY' ? '↑ ВВЕРХ' : '↓ ВНИЗ';
                            ldirEl.style.color = data.lgbmDirection === 'BUY' ? '#00e676' : '#ff1744';
                        }
                        const lconfEl = document.getElementById('lgbmConf');
                        if (lconfEl) lconfEl.innerText = data.lgbmConfidence + '%';
                        const accEl = document.getElementById('lgbmAcc');
                        if (accEl && data.lgbmAccuracy != null) {
                            accEl.innerText = 'Точность модели: ' + data.lgbmAccuracy + '%';
                            accEl.style.color = data.lgbmAccuracy >= 55 ? '#a78bfa' : 'var(--subtext)';
                        }
                    }

                    const probBars = pricesToBars(data.chartData, 16);
                    if (probBars.length) renderMiniChart('probChart', probBars, '');

                    renderDirSvg(data.direction);

                    const durBars = pricesToBars(data.chartData, 8);
                    if (durBars.length) renderMiniChart('durChart', durBars, '');

                    if(data.levels) {
                        const L = data.levels;
                        const renderLevel = (id, lv) => {
                            const resEl = document.getElementById(id);
                            if (resEl) {
                                resEl.className = `result ${lv.direction.toLowerCase()}`;
                                resEl.innerText = `${lv.direction === 'NEUTRAL' ? '\u2014' : lv.direction}`;
                            }
                            const lineEl = document.getElementById(id.replace('res', ''));
                            if (lineEl) {
                                lineEl.style.display = 'flex';
                            }
                        };
                        renderLevel('ll1res', L.level1);
                        renderLevel('ll2res', L.level2);
                        renderLevel('ll3res', L.level3);
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
                    switchResultTab('chart');
                    flashResults();

                }, remainingDelay);
            } catch(e) {
                stopStatusBar();
                sphere.classList.remove('analyzing');
                btn.disabled = false;
                btn.innerText = 'ПОЛУЧИТЬ АНАЛИЗ';
                const catchMsg = `• Длина токена: ${tg && tg.initData ? tg.initData.length : 0}\n• Платформа: ${tg ? tg.platform : 'unknown'}\n• Адрес: ${window.location.href}`;
                renderError(e.message, catchMsg);
            }
        };

    ";
    }
}
