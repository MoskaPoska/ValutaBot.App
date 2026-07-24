namespace ValutaBot.MiniApp;

public static partial class MiniAppUI
{
    public static string GetHtmlBody()
    {
        return @"

    <!-- Animated floating particles -->
    <div class='particle-field'>
        <div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div>
        <div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div>
        <div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div>
    </div>

    <div id='screen-home' class='app-screen active'>

        <div class='welcome-section' id='welcomeSec'>
            <div class='welcome-title'>Приветствую<br>на голодных<br>играх</div>
            <div id='mainSphere' class='sphere-container'>
                <div class='sphere-scene'>
                    <div class='sphere-particles'>
                        <div class='sp sp1'></div>
                        <div class='sp sp2'></div>
                        <div class='sp sp3'></div>
                        <div class='sp sp4'></div>
                        <div class='sp sp5'></div>
                        <div class='sp sp6'></div>
                        <div class='sp sp7'></div>
                        <div class='sp sp8'></div>
                    </div>
                    <div class='orbits'>
                        <div class='orbit o1'></div>
                        <div class='orbit o2'></div>
                        <div class='orbit o3'></div>
                    </div>
                    <div class='magic-ball'>
                        <div class='ball-line lh lh1'></div>
                        <div class='ball-line lh lh2'></div>
                        <div class='ball-line lv lv1'></div>
                        <div class='ball-line lv lv2'></div>
                        <div class='ball-glare'></div>
                        <div class='ball-glare-2'></div>
                        <div class='ball-arrow'>
                            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='3' stroke-linecap='round' stroke-linejoin='round' width='48' height='48'>
                                <polyline points='23 6 13.5 15.5 8.5 10.5 1 18'></polyline>
                                <polyline points='17 6 23 6 23 12'></polyline>
                            </svg>
                        </div>
                    </div>
                </div>
                <div class='base-stand'>
                    <div class='base-top'></div>
                    <div class='base-mid'></div>
                    <div class='base-bot'></div>
                </div>
            </div>
        </div>

        <div class='top-categories' id='topCategories'>
            <div class='top-cat-btn active' data-cat='fiat'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><circle cx='12' cy='12' r='9'/><path d='M7 9h6a2 2 0 0 1 0 4H7'/><path d='M10 5v2m0 8v2'/></svg>
                <div class='label'>Валюты</div>
            </div>
            <div class='cat-divider'></div>
            <div class='top-cat-btn' data-cat='commodities'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M12 2a8 8 0 0 0-8 8c0 5 8 12 8 12s8-7 8-12a8 8 0 0 0-8-8z'/><circle cx='12' cy='10' r='3'/></svg>
                <div class='label'>Сырьё</div>
            </div>
            <div class='cat-divider'></div>
            <div class='top-cat-btn' data-cat='crypto'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'/></svg>
                <div class='label'>Крипта</div>
            </div>
            <div class='cat-divider'></div>
            <div class='top-cat-btn' data-cat='stocks'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><polyline points='22 7 13.5 15.5 8.5 10.5 2 17'/><polyline points='16 7 22 7 22 13'/></svg>
                <div class='label'>Акции</div>
            </div>
        </div>

        <div class='selector-section'>
            <div class='sel-grid'>
                <div class='sel-group'>
                    <span class='sel-label'>Актив</span>
                    <div class='dropdown-trigger' id='assetBtn'>
                        <span class='dropdown-val' id='selectedAsset'>EUR/USD OTC</span>
                        <span class='dropdown-arrow'>▼</span>
                    </div>
                </div>
                <div class='sel-group'>
                    <span class='sel-label'>Таймфрейм</span>
                    <div class='dropdown-trigger' id='tfBtn'>
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
                    <button class='tf-btn' data-tf='S5'>S5</button>
                    <button class='tf-btn' data-tf='S10'>S10</button>
                    <button class='tf-btn' data-tf='S15'>S15</button>
                    <button class='tf-btn' data-tf='S30'>S30</button>
                    <button class='tf-btn active' data-tf='M1'>M1</button>
                    <button class='tf-btn' data-tf='M3'>M3</button>
                    <button class='tf-btn' data-tf='M5'>M5</button>
                    <button class='tf-btn' data-tf='M30'>M30</button>
                    <button class='tf-btn' data-tf='H1'>H1</button>
                    <button class='tf-btn' data-tf='H4'>H4</button>
                </div>
            </div>

            <div class='candle-countdown'>
                <span class='label'>До закрытия свечи</span>
                <span class='time' id='candleTime'>--</span>
            </div>

            <button class='btn-analyze' id='btnGet'>ПОЛУЧИТЬ АНАЛИЗ</button>
            <div id='errorDisplay' class='error-box' style='display:none'></div>

            <div class='status-bar' id='statusBar'>
                <div class='sb-text'>
                    <div class='sb-title' id='sbTitle'>АНАЛИЗИРУЮ РЫНОК<span class='blink'>.</span></div>
                    <div class='sb-sub' id='sbSub'>ЗАГРУЗКА ДАННЫХ</div>
                </div>
                <div class='sb-bars'><div class='sbb'></div><div class='sbb'></div><div class='sbb'></div><div class='sbb'></div><div class='sbb'></div></div>
            </div>
        </div>

        <div class='results-grid' id='resultsGrid' style='display:none'>
            <div class='res-card'>
                <div class='res-label'>Вероятность</div>
                <div class='res-value' id='resProb' style='color:var(--accent)'>--%</div>
                <div class='res-chart' id='probChart'></div>
            </div>
            <div class='res-card'>
                <div class='res-label'>Направление</div>
                <div class='res-value' id='resDir' style='color:var(--subtext)'>--</div>
                <div class='res-dir-chart' id='dirChart'>
                    <svg viewBox='0 0 80 40'><path d='M10 35 L40 5 L70 35' stroke='var(--dim)' stroke-width='2.5' fill='none' stroke-linecap='round' stroke-linejoin='round' opacity='0.3'/></svg>
                </div>
            </div>
            <div class='res-card'>
                <div class='res-label'>Время</div>
                <div class='res-value' id='resDur' style='color:var(--subtext)'>--</div>
                <div class='res-chart' id='durChart'></div>
            </div>
        </div>

        <!-- Tab Bar -->
        <div class='tab-bar' id='resultsTabBar' style='display:none'>
            <div class='tab-btn active' id='tabBtnChart'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:12px;height:12px;margin-right:4px'><path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'/><polyline points='22 4 12 14.01 9 11.01'/></svg>
                Прогноз
            </div>
            <div class='tab-btn' id='tabBtnAI'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' style='width:12px;height:12px;margin-right:4px'><path d='M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z'/><polyline points='3.27 6.96 12 12.01 20.73 6.96'/><line x1='12' y1='22.08' x2='12' y2='12'/></svg>
                ИИ Аналитика
            </div>
        </div>

        <!-- Tab 2: AI Details and Technical Levels -->
        <div id='tabContentAI' style='display:none'>
            <!-- System Breakdown Cards (Claude AI + LightGBM ML) -->
            <div class='news-card' id='claudeCard' style='display:none;margin-bottom:8px'>
                <div class='news-header'>
                    <span class='news-badge' id='aiModelBadge'>🧠 AI</span>
                    <span class='news-label'>анализ графика</span>
                    <span class='news-sentiment' id='claudeSentiment'>--</span>
                </div>
                <div class='news-summary' id='claudeReasoning' style='max-height:140px;overflow-y:auto;scrollbar-width:thin;padding-right:4px;font-size:10.5px;line-height:1.45;color:var(--subtext);white-space:pre-wrap'></div>
            </div>


            <!-- Monte Carlo & Risk Management Card -->
            <div class='ml-card' id='mcCard' style='display:none;margin-bottom:8px;background:rgba(16,185,129,0.06);border:1px solid rgba(16,185,129,0.25)'>
                <div class='ml-header' style='display:flex;justify-content:space-between;align-items:center'>
                    <div><span class='ml-badge' style='background:linear-gradient(135deg,#10b981,#059669)'>🎰 Монте-Карло</span><span class='ml-label'>Риск &amp; Матожидание</span></div>
                    <span style='font-size:9px;color:#10b981;font-weight:700' id='mcSimCount'>1,000 прогонов</span>
                </div>
                <div style='display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-top:6px;padding:4px 0'>
                    <div style='background:rgba(255,255,255,0.03);padding:6px;border-radius:6px;text-align:center'>
                        <div style='font-size:9px;color:var(--subtext)'>Матожидание (EV)</div>
                        <div style='font-size:11.5px;font-weight:700;color:#10b981;margin-top:2px' id='mcEv'>--</div>
                    </div>
                    <div style='background:rgba(255,255,255,0.03);padding:6px;border-radius:6px;text-align:center'>
                        <div style='font-size:9px;color:var(--subtext)'>Риск по Келли</div>
                        <div style='font-size:11.5px;font-weight:700;color:#f59e0b;margin-top:2px' id='mcKelly'>--</div>
                    </div>
                </div>
            </div>

            <!-- Indicators Grid -->
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
                    <div class='res-label'>Order Flow</div>
                    <div class='res-value' id='resVol' style='color:var(--subtext);font-size:14px'>--</div>
                    <div class='res-chart' id='volChart'></div>
                </div>
            </div>

            <div class='ml-card' id='mlCard' style='display:none'>
                <div class='ml-header'><span class='ml-badge'>🧠 ML</span><span class='ml-label'>Прогноз нейросети</span></div>
                <div class='ml-body'>
                    <span class='ml-dir' id='mlDir'>--</span>
                    <span class='ml-conf' id='mlConf'>--%</span>
                </div>
            </div>

            <div class='levels-bar' id='levelsBar' style='margin-top:10px'>
                <div class='level-line' id='ll1'><span class='tag l1'>L1</span><span class='info'>Индикаторы</span><span class='result' id='ll1res'></span></div>
                <div class='level-line' id='ll2'><span class='tag l2'>L2</span><span class='info'>S/R + Объём</span><span class='result' id='ll2res'></span></div>
                <div class='level-line' id='ll3'><span class='tag l3'>L3</span><span class='info'>Мульти-ТФ</span><span class='result' id='ll3res'></span></div>
                <div class='levels-divider'></div>
                <div class='levels-total'><span id='ltotalVotes'>--</span><span class='dir' id='ltotalDir'>--</span></div>
            </div>
        </div>
    </div>
    ";
    }
}
