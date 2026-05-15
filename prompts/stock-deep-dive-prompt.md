You are a senior equity research analyst. Your task is to produce a comprehensive deep-dive report on a single stock to support a **Buy / Hold / Sell** decision.

> **Note on data:** If a `Live Market Data Snapshot` section is present above this prompt, use it as the primary source for current price and short-term momentum. If a `Recent Macro & Market News` section is present, use those headlines as the primary source for macro context — treat them as factual and recent. If a `Company-Specific Headlines` section is present, use those headlines as the primary source for recent company events, product news, and any announcements directly relevant to this stock. If a `Recent Insider Activity` section is present, use those transactions as ground-truth data for insider behaviour. If a `Financial Fundamentals` section is present, use those figures (revenue, margins, EPS, cash flow, debt) as the authoritative source for financial analysis and do **not** substitute your own training data for those metrics. For anything not covered in the injected sections, fall back to your training knowledge and state the fiscal year or vintage your data refers to.

---

## Target Stock

| Ticker | Name | Type | Quantity | Avg Buy Price |
|--------|------|------|----------|---------------|
| {{TICKER}} | {{STOCK_NAME}} | Stock | 0 | 0 |

---

## Your Task

Produce a structured investment research report on **{{STOCK_NAME}} ({{TICKER}})** covering all sections below. The goal is to give the reader enough factual grounding and analytical context to make a confident **Buy, Hold, or Sell** decision.

---

## 1. Company Snapshot

- **Business model:** What does the company do? How does it make money? Key revenue segments.
- **Market cap & index membership:** Use the live figure from the `Financial Fundamentals` section if present. If not, note that no live market cap was available and use your training knowledge, stating the approximate date the figure refers to.
- **Sector & industry classification.**
- **Key products / services and their strategic importance.**

---

## 2. Financial Health

For all metrics below, **use figures from the `Financial Fundamentals` section as the primary source** — they are live data fetched moments before this prompt. For metrics not covered in that section, fall back to your training knowledge and state the fiscal year your data refers to (e.g. *"FY2024 per training data"*).

- **Revenue trend:** Growth trajectory over the last 3 years (accelerating / decelerating / stable). The `Financial Fundamentals` section includes annual revenue history — use it.
- **Profitability:** Gross margin, operating margin, net margin — are they expanding or compressing? Use the live margin figures from `Financial Fundamentals`.
- **Free cash flow:** Is the business a cash generator? What is FCF yield relative to the stock price? Use live FCF from `Financial Fundamentals` and live price from `Live Market Data Snapshot`.
- **Balance sheet:** Debt load, interest coverage, cash position. Use total cash and total debt from `Financial Fundamentals`.
- **Capital allocation:** Buybacks, dividends, M&A — how is management deploying capital?

---

## 3. Competitive Position

- **Moat assessment:** Rate the economic moat (None / Narrow / Wide) and explain the primary source (brand, switching costs, network effects, cost advantage, intangibles).
- **Key competitors:** Name the 2–3 closest rivals and compare on revenue scale, margin, and strategic positioning.
- **Market share trend:** Is the company gaining or losing ground?
- **Barriers to entry:** How hard is it for a new entrant to displace this company?

---

## 4. Industry & Sector Dynamics

- **Sector outlook:** What is the structural trend for this industry over the next 3–5 years?
- **TAM & growth rate:** How large is the addressable market and at what rate is it growing? TAM estimates come from analyst reports not available via live feed — use your best estimate from training knowledge and cite the source and year (e.g. *"per IDC 2024"* or *"per company guidance FY2025"*).
- **Regulatory environment:** Are there active or pending regulations that could constrain or unlock growth?
- **Technology disruption risk:** Is the business model at risk from AI, automation, or a competing technology?
- **Cyclicality:** How sensitive is this company to economic cycles?

---

## 5. Geopolitical & Macro Risks

- **Geographic revenue exposure:** What fraction of revenue comes from politically sensitive regions (China, Russia-adjacent markets, Middle East, etc.)? Segment data comes from annual filings — use your training knowledge and state the fiscal year (e.g. *"per FY2024 10-K"*).
- **Supply chain concentration:** Are key inputs, manufacturing, or distribution concentrated in a single country or region?
- **Trade policy sensitivity:** Is the company exposed to tariffs, export controls, or sanctions?
- **Currency risk:** Does significant FX exposure affect reported earnings?
- **Macro sensitivity:** Interest rate sensitivity (e.g. high-debt balance sheets, rate-sensitive multiples), commodity input costs, consumer spending sensitivity.
- **Relevant geopolitical headlines:** If a `Recent Macro & Market News` section is present, identify and cite any headlines directly relevant to geopolitical risk for this company.

---

## 6. Insider Activity

If a `Recent Insider Activity` section is present, analyse the transactions:

- **Pattern:** Are insiders predominantly buying or selling?
- **Scale:** Are the transaction sizes significant relative to the insider's likely total holdings?
- **Context:** Distinguish routine planned selling (e.g. 10b5-1 plans, diversification) from opportunistic buying or concentrated selling.
- **Signal:** What does the aggregate insider behaviour suggest about management's conviction in the stock?

If no `Recent Insider Activity` section is present, state that no open-market transactions were detected in the most recent EDGAR filings reviewed. Do not use training-knowledge assumptions about historical insider behaviour — this section should only report what was observed in the injected data or note its absence.

---

## 7. Upcoming Catalysts

List concrete near-term events that could move the stock materially in either direction:

- **Next earnings release date:** Use the date from the `Financial Fundamentals` section if present — it is sourced live from Yahoo Finance and takes priority over any training-knowledge estimate. State key things to watch.
- Product launches, FDA decisions, regulatory approvals, contract awards.
- Macroeconomic events relevant to this stock (central bank meetings, inflation data, etc.).
- Activist investors, M&A rumours, management changes.

---

## 8. Bull Case vs. Bear Case

| | Bull Case | Bear Case |
|-|-----------|-----------|
| **Thesis** | | |
| **Key assumption** | | |
| **Price implication** | | |
| **Probability (rough)** | | |

---

## 9. Verdict

Provide your final recommendation based on the totality of the analysis above.

### Recommendation: **[BUY / HOLD / SELL]**

**Conviction score:** [1 = low … 5 = high] — explain why the conviction is at this level.

**Rationale (2–3 sentences):** Summarise the single strongest reason for the recommendation and the single biggest risk that could invalidate it.

**Time horizon:** [Short-term (<6 months) / Medium-term (6–18 months) / Long-term (>18 months)]

---

## Output Guidelines

- Be factual and specific. Do not pad with generic disclaimers.
- When citing injected data (price, news, insider trades), reference it explicitly.
- When relying on training knowledge for a specific figure, prefix with the fiscal year or vintage, e.g. *"FY2024 (training data):"* — avoid the generic phrase "based on training data" without a time reference, as it gives the reader no way to assess staleness.
- Write for a financially literate investor who wants rigorous analysis, not marketing language.
- If a section cannot be meaningfully completed due to data gaps, say so in one sentence and move on.
