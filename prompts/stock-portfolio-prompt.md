You are a professional investment analyst delivering a weekly portfolio review.

> **Note on data:** Your analysis is based on your training knowledge and any patterns you can reason about. You do not have live market data. Focus on fundamental analysis, known recent trends up to your knowledge cutoff, and qualitative reasoning about each position.

---

## My Portfolio

Fill in your actual holdings below. Remove placeholder rows and add your own.

| Ticker | Name                        | Type        | Quantity | Avg Buy Price (€) |
|--------|-----------------------------|-------------|------------|---------------|
| VOO    | Vanguard S&P 500 ETF ACC        | ETF         | 29        | 95.68          |
| MWRE    | Core MSCI World ETF ACC        | ETF         | 17.9        | 131.11          |
| ABEA    | Alphabet (A)       | Stock         | 2        | 135.50          |
| NVO    | Novo Nordisk (B)        | Stock         | 18        | 51.62          |
| VWCG    | FTSE Developed Europe EUR ACC        | ETF         | 10        | 49.50          |
| RY6    | Realty Income        | Stock         | 10        | 50.11          |
| ICGA    | MSCI China USD ACC        | ETF         | 100        | 4.90          |
| BRYN    | Berkshire Hathaway        | Stock         | 1        | 400         |
| 8PSG    | Pyshical Gold USD ACC        | Commodity         | 1        | 400.92         |
| MSF   | Microsoft                   | Stock       | 1        | 340.85          |
| 013A   | JD.com ADR                       | Stock       | 10        | 23.70          |
| NVD   | NVIDIA                      | Stock       | 1        | 161          |
| 3CP   | Xiaomi                      | Stock       | 55        | 3.76          |
| 1KN   | Vici Properties                      | Stock       | 6        | 25.17          |
| MIGA   | MicroStategy (A)                      | Stock       | 1        | 150.95          |
| CON   | Continental                      | Stock       | 2        | 56.99          |
| VZLC   | Physical Silver USD             | Commodity       | 2        | 70.60          |
| QVD5   | MSCI India USD ACC             | ETF       | 10       | 8.1          |
| 8XP   | Xpeng             | Stock       | 10        | 8.1          |
| IS0D   | Oil & Gas USD ACC             | ETF       | 2        | 27.67          |
| BABA   | Alibaba ADR             | Stock       | 4        | 100          |
| EDP   | EDP SA             | Stock       | 118        | 3.65          |
| UNH   | UnitedHealth             | Stock       | 1        | 300          |
| QVDE   | iShares SP500 inf tech             | ETF       | 15        | 22.85          |
| EXH1   | iShares STOXX eur 600 oil & gas DE             | ETF       | 1        | 30          |
| EXV9   | iShares STOXX eur 600 Travel & Leisure DE             | ETF       | 3        | 19.8          |
| VWCE   | Vanguard FTSE All-World ACC             | ETF       | 99        | 120.11          |
| BTC   | Bitcoin             | Crypto       | 0.01276        | 32041          |
| ETH   | Ethereum             | Crypto       | 0.1063        | 205.45         |

---

## Your Task

Analyse the portfolio above and produce a structured weekly review following the output format below.

### Analysis requirements

**For every position:**
- Recent price trend and momentum (qualitative, based on known context)
- Overall sentiment and any known headwinds or tailwinds
- Hold / Reinforce / Consider Selling recommendation with a clear one-sentence rationale

**For each individual stock (not ETFs or crypto):**
- Key valuation metrics: P/E ratio, forward P/E, EV/EBITDA (if known)
- Revenue and EPS growth trajectory (YoY)
- Profit margins and balance sheet strength (debt/equity)
- Comparison against the 2–3 closest competitors and the sector average on the above metrics
- Analyst consensus and any notable recent developments
- Explicit recommendation: **Reinforce** / **Hold** / **Consider Selling**

**For ETFs:**
- Comment on the underlying index exposure and any sector tilts
- Whether current macro environment favours or challenges this allocation

**For crypto:**
- Macro and on-chain context (where relevant)
- Risk-adjusted view within the portfolio

---

## Output Format

### 📊 Portfolio Snapshot
One short paragraph summarising overall portfolio health, diversification, and any concentration risks.

### 🔍 Position-by-Position Analysis

Repeat the following block for each holding:

#### [TICKER] — [Name]
- **Trend:** ...
- **Key metrics:** ...  *(stocks only)*
- **vs. Competitors:** ...  *(stocks only)*
- **Recommendation:** Reinforce / Hold / Consider Selling
- **Rationale:** one sentence

---

### ⚖️ Portfolio-Level Observations
- Sector/asset-class concentration risks
- Correlation between positions (e.g. tech-heavy overlap)
- Suggested rebalancing actions, if any

### 📋 Action Summary

| Ticker | Recommendation    | Priority |
|--------|-------------------|----------|
| ...    | ...               | High / Medium / Low |

---

## Guidelines
- Be direct and opinionated. Do not hedge every sentence.
- Base recommendations on fundamentals and known recent context, not speculation.
- Flag clearly when a recommendation is uncertain due to data limitations.
- Write for a self-directed retail investor who understands basic finance.
