namespace ValutaBot.MiniApp;

public static partial class MiniAppUI
{
    public static string GetHtmlBody()
    {
        return @"
    <div class='header'>
        <div class='header-title'>TradeBE SMART ALGO</div>
        <div class='header-status'><span class='status-dot'></span> <span id='statusText'>ОНЛАЙН (0 мс)</span></div>
    </div>

    <!-- Sphere Sphere Display -->
    <div class='sphere-container' id='sphereContainer'>
        <div class='sphere-wrapper' onclick='analyzeMarket()'>
            <div class='glow-layer'></div>
            <div class='orb-core'>
                <div class='sphere-particles'>
                    <div class='sp sp1'></div><div class='sp sp2'></div><div class='sp sp3'></div><div class='sp sp4'></div>
                    <div class='sp sp5'></div><div class='sp sp6'></div><div class='sp sp7'></div><div class='sp sp8'></div>
                </div>
                <div class='orb-inner'></div>
                <div class='scan-wave'></div>
                <div class='sphere-content'>
                    <div class='sphere-direction' id='sphereDir'>--</div>
                    <div class='sphere-prob' id='sphereProb'>--%</div>
                    <div class='sphere-reason' id='sphereReason'>НАЖМИТЕ ДЛЯ АНАЛИЗА</div>
                </div>
                <div class='bot-mascot'>
                    <div class='antenna'><div class='ball'></div></div>
                    <div class='head'>
                        <div class='eye left'></div>
                        <div class='eye right'></div>
                        <div class='mouth'></div>
                    </div>
                    <div class='body-bot'></div>
                    <div class='base-bot'></div>
                </div>
            </div>
        </div>
    </div>

    <div class='top-categories' id='topCategories'>
        <div class='top-cat-btn active' data-cat='fiat' onclick='changeTopCategory(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><circle cx='12' cy='12' r='9'/><path d='M7 9h6a2 2 0 0 1 0 4H7'/><path d='M10 5v2m0 8v2'/></svg>
            <div class='label'>Валюты</div>
        </div>
        <div class='cat-divider'></div>
        <div class='top-cat-btn' data-cat='commodities' onclick='changeTopCategory(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M12 2a8 8 0 0 0-8 8c0 5 8 12 8 12s8-7 8-12a8 8 0 0 0-8-8z'/><circle cx='12' cy='10' r='3'/></svg>
            <div class='label'>Сырьё</div>
        </div>
        <div class='cat-divider'></div>
        <div class='top-cat-btn' data-cat='crypto' onclick='changeTopCategory(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'/></svg>
            <div class='label'>Крипта</div>
        </div>
        <div class='cat-divider'></div>
        <div class='top-cat-btn' data-cat='stocks' onclick='changeTopCategory(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><polyline points='22 7 13.5 15.5 8.5 10.5 2 17'/><polyline points='16 7 22 7 22 13'/></svg>
            <div class='label'>Акции</div>
        </div>
    </div>

    <div class='selector-section'>
        <div class='sel-grid'>
            <div class='sel-group'>
                <span class='sel-label'>Актив</span>
                <div class='dropdown-trigger' id='assetBtn' onclick='toggleMenu(""assetMenu"", ""assetBtn"")'>
                    <span class='dropdown-val' id='selectedAsset'>EUR/USD OTC</span>
                    <span class='dropdown-arrow'>▼</span>
                </div>
            </div>
            <div class='sel-group'>
                <span class='sel-label'>Таймфрейм</span>
                <div class='dropdown-trigger' id='tfBtn' onclick='toggleMenu(""tfMenu"", ""tfBtn"")'>
                    <span class='dropdown-val' id='selectedTf'>M1</span>
                    <span class='dropdown-arrow'>▼</span>
                </div>
            </div>
        </div>

        <div class='asset-menu' id='assetMenu'>
            <div class='asset-grid' id='assetGrid'></div>
        </div>

        <div class='tf-menu' id='tfMenu'>
            <div class='tf-grid'>
                <div class='tf-btn active' data-tf='m1' onclick='setTf(this)'>M1</div>
                <div class='tf-btn' data-tf='s5' onclick='setTf(this)'>S5</div>
                <div class='tf-btn' data-tf='s10' onclick='setTf(this)'>S10</div>
                <div class='tf-btn' data-tf='s15' onclick='setTf(this)'>S15</div>
                <div class='tf-btn' data-tf='s30' onclick='setTf(this)'>S30</div>
                <div class='tf-btn' data-tf='m5' onclick='setTf(this)'>M5</div>
            </div>
        </div>

        <button class='btn-analyze' id='btnAnalyze' onclick='analyzeMarket()'>ПОЛУЧИТЬ АНАЛИЗ</button>
    </div>

    <!-- Live Real-Time Tick Price Display -->
    <div class='live-price-container' id='livePriceBox'>
        <span class='live-price-dot'>●</span>
        <span class='live-price-label'>LIVE PRICE:</span>
        <span class='live-price-value' id='livePriceVal'>--.-----</span>
    </div>

    <div class='progress-section' id='progressSection' style='display:none'>
        <div class='progress-card'>
            <div class='progress-header'>
                <span class='progress-title'>Нейросеть сканирует рынок...</span>
                <span class='progress-pct' id='progressPct'>0%</span>
            </div>
            <div class='progress-bar-track'>
                <div class='progress-bar-fill' id='progressBarFill'></div>
            </div>
            <div class='progress-steps' id='progressSteps'>
                <div class='progress-step' id='pstep1'><span class='step-icon'>✓</span> Сбор тиков WebSockets</div>
                <div class='progress-step' id='pstep2'><span class='step-icon'>✓</span> Gatekeeper & SMC-анализ</div>
                <div class='progress-step' id='pstep3'><span class='step-icon'>✓</span> LightGBM & Order Flow</div>
                <div class='progress-step' id='pstep4'><span class='step-icon'>✓</span> Расчёт экспирации</div>
            </div>
        </div>
    </div>

    <div id='errorBanner' class='error-card' style='display:none'>
        <div class='error-icon'>⚠️</div>
        <div class='error-title' id='errTitle'>Ошибка получения данных</div>
        <div class='error-desc' id='errDesc'>Не удалось связаться с сервером. Попробуйте еще раз.</div>
        <button class='error-retry-btn' onclick='analyzeMarket()'>Повторить попытку</button>
        <div style='margin-top:10px;'>
            <span class='error-debug-toggle' onclick='toggleErrorDebug()'>📋 Подробности отладки</span>
            <div id='errorDebugBox' class='error-debug-content' style='display:none'></div>
        </div>
    </div>

    <div id='resultsSection' style='display:none'>
        <!-- Expiry Recommendation Card -->
        <div class='results-grid' style='grid-template-columns:1fr;margin-bottom:8px'>
            <div class='res-card' style='min-height:55px;padding:8px 12px;flex-direction:row;align-items:center;justify-content:space-between'>
                <div style='text-align:left'>
                    <div class='res-label' style='margin-bottom:2px'>РЕКОМЕНДУЕМАЯ ЭКСПИРАЦИЯ</div>
                    <div class='res-value' id='resDurationText' style='color:#00e5ff;font-size:15px'>--</div>
                </div>
                <div class='res-chart' id='durChart'></div>
            </div>
        </div>

        <!-- Tab Bar -->
        <div class='tab-bar' id='resultsTabBar' style='display:none'>
            <div class='tab-btn active' id='tabBtnChart' onclick=""switchResultTab('chart')"">
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:12px;height:12px;margin-right:4px'><path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'/><polyline points='22 4 12 14.01 9 11.01'/></svg>
                Прогноз
            </div>
            <div class='tab-btn' id='tabBtnAI' onclick=""switchResultTab('ai')"">
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:12px;height:12px;margin-right:4px'><path d='M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z'/><polyline points='3.27 6.96 12 12.01 20.73 6.96'/><line x1='12' y1='22.08' x2='12' y2='12'/></svg>
                ИИ Аналитика
            </div>
        </div>

        <!-- Tab 2: AI Details and Technical Levels -->
        <div id='tabContentAI' style='display:none'>
            <div class='news-card' id='claudeCard' style='display:none;margin-bottom:8px'>
                <div class='news-header'>
                    <span class='news-badge' id='aiModelBadge'>🧠 AI</span>
                    <span class='news-label'>анализ графика</span>
                    <span class='news-sentiment' id='claudeSentiment'>--</span>
                </div>
                <div class='news-summary' id='claudeReasoning' style='max-height:140px;overflow-y:auto;scrollbar-width:thin;padding-right:4px;font-size:10.5px;line-height:1.45;color:var(--subtext);white-space:pre-wrap'></div>
            </div>

            <div class='ml-card' id='lgbmCard' style='display:none;margin-bottom:8px'>
                <div class='ml-header'><span class='ml-badge' style='background:linear-gradient(135deg,#f59e0b,#d97706)'>⚡ ML</span><span class='ml-label'>LightGBM локальная ИИ</span></div>
                <div class='ml-body'>
                    <span class='ml-dir' id='lgbmDir'>--</span>
                    <span class='ml-conf' id='lgbmConf' style='font-size:11px'>--%</span>
                </div>
                <div style='font-size:9px;color:var(--subtext);text-align:center;margin-top:2px;padding-bottom:4px' id='lgbmAcc'></div>
            </div>

            <div class='results-grid' style='margin-bottom:12px;margin-top:4px'>
                <div class='res-card'>
                    <div class='res-label'>RSI</div>
                    <div class='res-value' id='resRsi' style='color:var(--subtext);font-size:16px'>--</div>
                    <div class='res-chart' id='rsiChart'></div>
                </div>
                <div class='res-card'>
                    <div class='res-label'>EMA (9)</div>
                    <div class='res-value' id='resEma' style='color:var(--subtext);font-size:16px'>--</div>
                    <div class='res-chart' id='emaChart'></div>
                </div>
                <div class='res-card'>
                    <div class='res-label'>Объём</div>
                    <div class='res-value' id='resVol' style='color:var(--subtext);font-size:16px'>--</div>
                    <div class='res-chart' id='volChart'></div>
                </div>
            </div>

            <div class='levels-bar' id='levelsBar'>
                <div class='level-line' id='ll1'><span class='tag l1'>L1</span><span class='info'>Индикаторы</span><span class='result' id='ll1res'></span></div>
                <div class='level-line' id='ll2'><span class='tag l2'>L2</span><span class='info'>S/R + Объём</span><span class='result' id='ll2res'></span></div>
                <div class='level-line' id='ll3'><span class='tag l3'>L3</span><span class='info'>Мульти-ТФ</span><span class='result' id='ll3res'></span></div>
                <div class='levels-divider'></div>
                <div class='levels-total'><span id='ltotalVotes'>--</span><span class='dir' id='ltotalDir'>--</span></div>
            </div>
        </div>
    </div>";
    }
}
