namespace ValutaBot.MiniApp;

public static partial class MiniAppUI
{
    public static string GetCssStyles()
    {
        return @"
        @import url('https://fonts.googleapis.com/css2?family=Unbounded:wght@600;700;800;900&family=Inter:wght@400;600;700;800&display=swap');
        :root {
            --bg: #07051a;
            --panel: rgba(12, 10, 35, 0.88);
            --panel-border: rgba(124, 77, 255, 0.18);
            --accent: #7c4dff;
            --accent-glow: rgba(124, 77, 255, 0.35);
            --magenta: #b388ff;
            --cyan: #00e5ff;
            --gold: #ffd700;
            --green: #00e676;
            --red: #ff1744;
            --text: #ffffff;
            --subtext: #a89fd4;
            --dim: #5a5290;
            --radius: 12px;
            --btn-h: 46px;
            --glass-bg: rgba(255, 255, 255, 0.03);
        }
        * { box-sizing: border-box; margin: 0; padding: 0; -webkit-tap-highlight-color: transparent; }
        body {
            background: radial-gradient(ellipse at 50% -30%, #2d1060 0%, #0c0925 35%, #060412 70%, #03020a 100%);
            color: var(--text);
            padding: 12px;
            padding-bottom: 24px;
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
            user-select: none;
            font-size: 14px;
            line-height: 1.4;
            min-height: 100vh;
            overflow-x: hidden;
        }

        body::before {
            content: '';
            position: fixed;
            inset: 0;
            background:
                radial-gradient(ellipse at 15% 60%, rgba(124,77,255,0.07) 0%, transparent 50%),
                radial-gradient(ellipse at 80% 20%, rgba(0,229,255,0.04) 0%, transparent 40%),
                radial-gradient(ellipse at 50% 80%, rgba(179,136,255,0.05) 0%, transparent 40%);
            pointer-events: none;
            z-index: 0;
        }

        .dropdown-trigger {
            background: rgba(255,255,255,0.03);
            border: 1px solid var(--panel-border);
            height: var(--btn-h);
            padding: 0 12px;
            border-radius: var(--radius);
            display: flex;
            justify-content: space-between;
            align-items: center;
            cursor: pointer;
            transition: all 0.25s;
            position: relative;
        }
        .dropdown-trigger:hover { border-color: rgba(124,77,255,0.3); background: rgba(124,77,255,0.05); }
        .dropdown-val { font-size: 14px; font-weight: 700; color: #fff; }
        .dropdown-arrow { font-size: 10px; color: var(--dim); }

        .asset-menu, .tf-menu {
            display: none;
            position: absolute;
            top: calc(100% + 8px);
            left: 0;
            width: 100%;
            background: #080618;
            backdrop-filter: blur(30px);
            -webkit-backdrop-filter: blur(30px);
            border: 1px solid var(--panel-border);
            border-radius: 20px;
            padding: 18px;
            z-index: 100;
            box-shadow: 0 20px 60px rgba(0,0,0,0.9);
            animation: menuIn 0.2s ease-out;
        }
        @keyframes menuIn { from { opacity: 0; transform: translateY(-8px); } to { opacity: 1; transform: translateY(0); } }
        .asset-menu.show, .tf-menu.show { display: block; }
        .asset-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
        .asset-item { background: rgba(255,255,255,0.03); color: var(--subtext); padding: 13px 0; text-align: center; border-radius: 12px; font-size: 12px; font-weight: 700; cursor: pointer; margin-bottom: 6px; border: 1px solid transparent; transition: all 0.25s cubic-bezier(0.34, 1.56, 0.64, 1); }
        .asset-item:hover { background: rgba(124,77,255,0.08); border-color: rgba(124,77,255,0.2); }
        .asset-item.active { background: linear-gradient(135deg, var(--accent), #6a3de8); color: #fff; border-color: var(--accent); box-shadow: 0 4px 20px var(--accent-glow); }
        .asset-item.major { border-color: rgba(124,77,255,0.25); background: linear-gradient(135deg, rgba(124,77,255,0.08), rgba(0,229,255,0.02)); box-shadow: 0 0 12px rgba(124,77,255,0.08); }

        .otc-scroll { max-height: 340px; overflow-y: auto; scrollbar-width: thin; scrollbar-color: rgba(124,77,255,0.3) transparent; scroll-behavior: smooth; }
        .otc-scroll::-webkit-scrollbar { width: 3px; }
        .otc-scroll::-webkit-scrollbar-track { background: transparent; }
        .otc-scroll::-webkit-scrollbar-thumb { background: rgba(124,77,255,0.3); border-radius: 3px; }

        .tf-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; }
        .tf-btn {
            background: rgba(255,255,255,0.03);
            border: 1px solid var(--panel-border);
            height: 48px;
            color: var(--subtext);
            border-radius: 12px;
            cursor: pointer;
            font-size: 13px;
            font-weight: 700;
            transition: all 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
        }
        .tf-btn:hover { background: rgba(124,77,255,0.08); border-color: rgba(124,77,255,0.2); }
        .tf-btn.active { background: linear-gradient(135deg, var(--accent), #6a3de8); color: #fff; border-color: var(--accent); box-shadow: 0 4px 20px var(--accent-glow); }

        .btn-analyze {
            width: 100%;
            height: var(--btn-h);
            background: linear-gradient(135deg, #7c4dff 0%, #b388ff 30%, #00e5ff 100%);
            background-size: 200% 200%;
            border: none;
            color: white;
            border-radius: var(--radius);
            font-weight: 800;
            font-size: 14px;
            letter-spacing: 2px;
            text-transform: uppercase;
            cursor: pointer;
            margin-top: 10px;
            transition: all 0.35s cubic-bezier(0.34, 1.56, 0.64, 1);
            box-shadow: 0 4px 12px rgba(124, 77, 255, 0.2);
            position: relative;
            z-index: 1;
            overflow: hidden;
            animation: btnShimmer 4s ease-in-out infinite;
        }

        @keyframes btnShimmer {
            0% { background-position: 0% 50%; }
            50% { background-position: 100% 50%; }
            100% { background-position: 0% 50%; }
        }
        .btn-analyze::after {
            content: '';
            position: absolute;
            inset: 0;
            background: linear-gradient(135deg, transparent 40%, rgba(255,255,255,0.12) 60%, transparent 80%);
            transition: transform 0.6s;
            transform: translateX(-100%);
        }
        .btn-analyze:hover::after { transform: translateX(100%); }
        .btn-analyze:active { transform: scale(0.96); }
        .btn-analyze:disabled { opacity: 0.5; transform: none; box-shadow: none; animation: none; }

        .results-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 6px; margin-top: 8px; }
        .res-card {
            background: rgba(255,255,255,0.02);
            border: 1px solid var(--panel-border);
            border-radius: 12px;
            padding: 10px;
            text-align: center;
        }

        .status-bar {
            background: rgba(255,255,255,0.02);
            border: 1px solid var(--panel-border);
            border-radius: 14px;
            padding: 10px 14px;
            margin-top: 10px;
            align-items: center;
            gap: 12px;
            animation: sbIn 0.3s ease;
            pointer-events: auto;
            position: relative;
        }
        .status-bar.show { display: flex; }
        @keyframes sbIn { from { opacity: 0; transform: translateY(-6px); } to { opacity: 1; transform: translateY(0); } }
        .status-bar .sb-text { flex: 1; min-width: 0; }
        .status-bar .sb-title { font-size: 12px; font-weight: 700; color: var(--accent); letter-spacing: 1px; }
        .status-bar .sb-title .blink { animation: sbBlink 0.8s step-end infinite; }
        @keyframes sbBlink { 50% { opacity: 0; } }
        .status-bar .sb-sub { font-size: 10px; color: var(--dim); margin-top: 1px; font-weight: 600; letter-spacing: 0.5px; transition: opacity 0.25s; }
        .status-bar .sb-sub.fade { opacity: 0.3; }
        .status-bar .sb-bars { display: flex; gap: 3px; align-items: flex-end; height: 18px; flex-shrink: 0; }
        .status-bar .sb-bars .sbb {
            width: 3px; border-radius: 2px;
            background: linear-gradient(to top, var(--accent), #b388ff);
            animation: sbbWave 0.7s ease-in-out infinite alternate;
        }
        .status-bar .sb-bars .sbb:nth-child(1) { height: 4px; animation-delay: 0s; }
        .status-bar .sb-bars .sbb:nth-child(2) { height: 8px; animation-delay: 0.1s; }
        .status-bar .sb-bars .sbb:nth-child(3) { height: 14px; animation-delay: 0.2s; }
        .status-bar .sb-bars .sbb:nth-child(4) { height: 10px; animation-delay: 0.3s; }
        .status-bar .sb-bars .sbb:nth-child(5) { height: 6px; animation-delay: 0.4s; }
        @keyframes sbbWave { 0% { transform: scaleY(0.5); opacity: 0.4; } 100% { transform: scaleY(1); opacity: 1; } }

        .app-screen { display: none; }
        .app-screen.active { display: block; }

        .hist-header {
            font-family: 'Unbounded', sans-serif;
            font-size: 16px;
            font-weight: 800;
            margin-bottom: 12px;
            background: linear-gradient(135deg, #e0d0ff, #b388ff);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
        }
        .filter-row { display: flex; gap: 6px; margin-bottom: 14px; }
        .filter-btn {
            flex: 1; background: rgba(255,255,255,0.03); border: 1px solid var(--panel-border);
            color: var(--dim); padding: 8px 0; border-radius: 10px;
            font-size: 10px; font-weight: 700; cursor: pointer; transition: all 0.25s;
            position: relative;
        }
        .filter-btn.active { background: rgba(124,77,255,0.12); border-color: var(--accent); color: #fff; box-shadow: 0 0 12px rgba(124,77,255,0.15); }
        .top-star { font-size: 13px; line-height: 1; color: var(--gold); margin-left: 5px; display: inline-block; filter: drop-shadow(0 0 4px rgba(255,215,0,0.5)); }

        .live-price-container {
            display: none;
            justify-content: center;
            align-items: center;
            gap: 8px;
            font-size: 14px;
            font-weight: 600;
            color: var(--dim);
            font-family: 'Outfit', sans-serif;
            background: rgba(255,255,255,0.02);
            border: 1px solid rgba(255,255,255,0.05);
            padding: 8px 16px;
            border-radius: 20px;
            width: fit-content;
            margin: 10px auto 5px auto;
            box-shadow: 0 4px 15px rgba(0,0,0,0.15);
            transition: border-color 0.3s;
        }
        .live-price-dot {
            color: #00e676;
            font-size: 10px;
            animation: pulse 1.5s infinite;
        }
        .live-price-label {
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            opacity: 0.8;
        }
        .live-price-value {
            color: #ffffff;
            font-family: 'Outfit', monospace;
            font-size: 16px;
            transition: color 0.15s ease-out;
        }
        .live-price-value.up {
            color: #00e676 !important;
            text-shadow: 0 0 8px rgba(0,230,118,0.4);
        }
        .live-price-value.down {
            color: #ff1744 !important;
            text-shadow: 0 0 8px rgba(255,23,68,0.4);
        }
        @keyframes pulse {
            0% { opacity: 0.3; }
            50% { opacity: 1; }
            100% { opacity: 0.3; }
        }

        .sphere-container.buy-signal .magic-ball {
            background:
                repeating-linear-gradient(85deg,
                    transparent 0px, transparent 5px,
                    rgba(180,255,200,0.10) 5px, rgba(180,255,200,0.10) 6px
                ),
                repeating-radial-gradient(circle at 50% 50%,
                    transparent 0px, transparent 7px,
                    rgba(180,255,200,0.08) 7px, rgba(180,255,200,0.08) 8px
                ),
                radial-gradient(circle at 30% 35%, rgba(215,255,240,0.45) 0%, rgba(120,255,180,0.3) 20%, rgba(40,200,100,0.5) 45%, rgba(10,120,50,0.75) 70%, rgba(0,40,10,0.95) 100%);
            box-shadow:
                inset -10px 25px 45px rgba(255,255,255,0.08),
                inset 15px -25px 50px rgba(20,180,80,0.35),
                inset 0 -40px 50px rgba(0,0,0,0.35),
                0 0 65px rgba(0,230,118,0.35);
            animation: ball3dSpin 18s linear infinite, pulseGlowBuy 1.5s infinite ease-in-out;
        }
        @keyframes pulseGlowBuy {
            0% { box-shadow: inset -10px 25px 45px rgba(255,255,255,0.08), inset 15px -25px 50px rgba(20,180,80,0.35), inset 0 -40px 50px rgba(0,0,0,0.35), 0 0 50px rgba(0,230,118,0.35); }
            50% { box-shadow: inset -10px 25px 45px rgba(255,255,255,0.15), inset 15px -25px 50px rgba(0,230,118,0.45), inset 0 -40px 50px rgba(0,0,0,0.35), 0 0 80px rgba(0,230,118,0.7); }
            100% { box-shadow: inset -10px 25px 45px rgba(255,255,255,0.08), inset 15px -25px 50px rgba(20,180,80,0.35), inset 0 -40px 50px rgba(0,0,0,0.35), 0 0 50px rgba(0,230,118,0.35); }
        }

        .sphere-container.put-signal .magic-ball {
            background:
                repeating-linear-gradient(85deg,
                    transparent 0px, transparent 5px,
                    rgba(255,180,200,0.10) 5px, rgba(255,180,200,0.10) 6px
                ),
                repeating-radial-gradient(circle at 50% 50%,
                    transparent 0px, transparent 7px,
                    rgba(255,180,200,0.08) 7px, rgba(255,180,200,0.08) 8px
                ),
                radial-gradient(circle at 30% 35%, rgba(255,215,220,0.45) 0%, rgba(255,120,150,0.3) 20%, rgba(200,40,70,0.5) 45%, rgba(120,10,35,0.75) 70%, rgba(40,0,10,0.95) 100%);
            box-shadow:
                inset -10px 25px 45px rgba(255,255,255,0.08),
                inset 15px -25px 50px rgba(180,20,50,0.35),
                inset 0 -40px 50px rgba(0,0,0,0.35),
                0 0 65px rgba(255,23,68,0.35);
            animation: ball3dSpin 18s linear infinite, pulseGlowPut 1.5s infinite ease-in-out;
        }
        @keyframes pulseGlowPut {
            0% { box-shadow: inset -10px 25px 45px rgba(255,255,255,0.08), inset 15px -25px 50px rgba(180,20,50,0.35), inset 0 -40px 50px rgba(0,0,0,0.35), 0 0 50px rgba(255,23,68,0.35); }
            50% { box-shadow: inset -10px 25px 45px rgba(255,255,255,0.15), inset 15px -25px 50px rgba(255,23,68,0.45), inset 0 -40px 50px rgba(0,0,0,0.35), 0 0 80px rgba(255,23,68,0.7); }
            100% { box-shadow: inset -10px 25px 45px rgba(255,255,255,0.08), inset 15px -25px 50px rgba(180,20,50,0.35), inset 0 -40px 50px rgba(0,0,0,0.35), 0 0 50px rgba(255,23,68,0.35); }
        }

        .sphere-container.buy-signal .orbit.o1 {
            border-color: rgba(0, 230, 118, 0.85);
            box-shadow: 0 0 25px rgba(0, 230, 118, 0.6), inset 0 0 25px rgba(0, 230, 118, 0.4);
        }
        .sphere-container.buy-signal .orbit.o2 {
            border-color: rgba(180, 255, 200, 0.7);
            box-shadow: 0 0 20px rgba(180, 255, 200, 0.45), inset 0 0 20px rgba(180, 255, 200, 0.35);
        }
        .sphere-container.buy-signal .orbit.o3 {
            border-color: rgba(0, 230, 118, 0.6);
            box-shadow: 0 0 16px rgba(0, 230, 118, 0.35), inset 0 0 16px rgba(0, 230, 118, 0.25);
        }

        .sphere-container.put-signal .orbit.o1 {
            border-color: rgba(255, 23, 68, 0.85);
            box-shadow: 0 0 25px rgba(255, 23, 68, 0.6), inset 0 0 25px rgba(255, 23, 68, 0.4);
        }
        .sphere-container.put-signal .orbit.o2 {
            border-color: rgba(255, 180, 200, 0.7);
            box-shadow: 0 0 20px rgba(255, 180, 200, 0.45), inset 0 0 20px rgba(255, 180, 200, 0.35);
        }
        .sphere-container.put-signal .orbit.o3 {
            border-color: rgba(255, 23, 68, 0.6);
            box-shadow: 0 0 16px rgba(255, 23, 68, 0.35), inset 0 0 16px rgba(255, 23, 68, 0.25);
        }

        .sphere-particles {
            position: absolute;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            z-index: 5;
            pointer-events: none;
            overflow: visible;
        }
        .sp {
            position: absolute;
            width: 5px;
            height: 5px;
            border-radius: 50%;
            background: #a78bfa;
            opacity: 0;
            pointer-events: none;
            transition: background 0.5s, opacity 0.5s;
        }
        .sphere-container.buy-signal .sp {
            background: #00e676;
            opacity: 0.85;
            box-shadow: 0 0 8px #00e676;
        }
        .sphere-container.buy-signal .sp1 { animation: floatUp1 2.5s infinite linear; }
        .sphere-container.buy-signal .sp2 { animation: floatUp2 2.2s infinite linear; }
        .sphere-container.buy-signal .sp3 { animation: floatUp3 2.8s infinite linear; }
        .sphere-container.buy-signal .sp4 { animation: floatUp4 2.4s infinite linear; }
        .sphere-container.buy-signal .sp5 { animation: floatUp1 2.6s infinite linear 0.5s; }
        .sphere-container.buy-signal .sp6 { animation: floatUp2 2.3s infinite linear 0.7s; }
        .sphere-container.buy-signal .sp7 { animation: floatUp3 2.7s infinite linear 0.3s; }
        .sphere-container.buy-signal .sp8 { animation: floatUp4 2.5s infinite linear 0.6s; }

        @keyframes floatUp1 {
            0% { transform: translate(30px, 90px) scale(0.5); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(20px, -20px) scale(1.2); opacity: 0; }
        }
        @keyframes floatUp2 {
            0% { transform: translate(70px, 90px) scale(0.5); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(80px, -20px) scale(1.2); opacity: 0; }
        }
        @keyframes floatUp3 {
            0% { transform: translate(50px, 100px) scale(0.5); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(40px, -30px) scale(1.2); opacity: 0; }
        }
        @keyframes floatUp4 {
            0% { transform: translate(20px, 80px) scale(0.5); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(30px, -15px) scale(1.2); opacity: 0; }
        }

        .sphere-container.put-signal .sp {
            background: #ff1744;
            opacity: 0.85;
            box-shadow: 0 0 8px #ff1744;
        }
        .sphere-container.put-signal .sp1 { animation: floatDown1 2.5s infinite linear; }
        .sphere-container.put-signal .sp2 { animation: floatDown2 2.2s infinite linear; }
        .sphere-container.put-signal .sp3 { animation: floatDown3 2.8s infinite linear; }
        .sphere-container.put-signal .sp4 { animation: floatDown4 2.4s infinite linear; }
        .sphere-container.put-signal .sp5 { animation: floatDown1 2.6s infinite linear 0.5s; }
        .sphere-container.put-signal .sp6 { animation: floatDown2 2.3s infinite linear 0.7s; }
        .sphere-container.put-signal .sp7 { animation: floatDown3 2.7s infinite linear 0.3s; }
        .sphere-container.put-signal .sp8 { animation: floatDown4 2.5s infinite linear 0.6s; }

        @keyframes floatDown1 {
            0% { transform: translate(20px, -10px) scale(1.2); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(30px, 110px) scale(0.5); opacity: 0; }
        }
        @keyframes floatDown2 {
            0% { transform: translate(80px, -10px) scale(1.2); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(70px, 110px) scale(0.5); opacity: 0; }
        }
        @keyframes floatDown3 {
            0% { transform: translate(40px, -20px) scale(1.2); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(50px, 120px) scale(0.5); opacity: 0; }
        }
        @keyframes floatDown4 {
            0% { transform: translate(30px, -5px) scale(1.2); opacity: 0; }
            20% { opacity: 0.85; }
            100% { transform: translate(20px, 105px) scale(0.5); opacity: 0; }
        }

        .error-box {
            background: rgba(255, 23, 68, 0.08);
            border: 1px solid rgba(255, 23, 68, 0.25);
            border-radius: 16px;
            padding: 16px;
            margin-top: 14px;
            font-family: 'Inter', sans-serif;
            text-align: center;
            box-shadow: 0 4px 15px rgba(255, 23, 68, 0.05);
            animation: slideUp 0.3s ease-out;
        }
        .error-header {
            color: #ff3b30;
            font-weight: 700;
            font-size: 14px;
            margin-bottom: 6px;
            letter-spacing: 0.2px;
        }
        .error-desc {
            color: #ff6b6b;
            font-size: 12.5px;
            line-height: 1.4;
            font-weight: 500;
            margin-bottom: 10px;
        }
        .error-debug-toggle {
            display: inline-block;
            font-size: 11px;
            color: var(--dim);
            cursor: pointer;
            padding: 4px 10px;
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 12px;
            font-weight: 600;
            transition: all 0.2s;
            user-select: none;
        }
        .error-debug-toggle:hover {
            color: #ffffff;
            background: rgba(255, 255, 255, 0.06);
        }
        .error-debug-content {
            margin-top: 12px;
            padding: 12px;
            background: rgba(0, 0, 0, 0.3);
            border-radius: 10px;
            font-family: monospace;
            font-size: 10.5px;
            color: rgba(255, 255, 255, 0.5);
            text-align: left;
            white-space: pre-wrap;
            word-break: break-all;
            max-height: 100px;
            overflow-y: auto;
            border: 1px solid rgba(255, 255, 255, 0.05);
        }

        .sync-container {
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            font-size: 11.5px;
            font-weight: 600;
            color: var(--dim);
            background: rgba(255,255,255,0.02);
            border: 1px solid var(--panel-border);
            padding: 8px 14px;
            border-radius: 12px;
            margin: 10px 0 0 0;
            cursor: pointer;
            transition: all 0.3s ease;
        }
        .sync-container:hover {
            background: rgba(124,77,255,0.06);
            border-color: rgba(124,77,255,0.25);
            color: #fff;
        }
        .sync-status-dot {
            color: #ff1744;
            font-size: 10px;
            animation: pulse 1.5s infinite;
        }
        .sync-status-dot.online {
            color: #00e676 !important;
            filter: drop-shadow(0 0 4px #00e676);
        }
        .sync-status-text {
            flex: 1;
            text-align: left;
        }
        .sync-help-icon {
            font-size: 11px;
            opacity: 0.6;
        }";
    }
}
