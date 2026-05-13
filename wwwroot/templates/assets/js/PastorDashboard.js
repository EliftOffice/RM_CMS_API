
$(document).ready(function () {
    loadDashboard();
});

function loadDashboard() {

    $.ajax({
        url: API_BASE_URL + "/pastors/dashboard",
        method: "GET",
        success: function (res) {

            if (res.responseType === 0 && res.data) {

                const data = res.data;

                // HEADER
                renderHeader();

                // SYSTEM HEALTH
                if (data.systemHealth) {
                    renderSystemHealth(data.systemHealth);
                }
                if (res.data.kpIs) {
                    renderKPIs(res.data.kpIs);
                }
                if (res.data.teamLeadPerformance) {
                    renderTeamLeads(res.data.teamLeadPerformance);
                }

                if (res.data.pipelineHealth) {
                    renderPipeline(res.data.pipelineHealth);
                }

                if (res.data.escalations) {
                    renderEscalations(res.data.escalations);
                }

                if (res.data.trends) {
                    renderTrends(res.data.trends);
                }

                if (res.data.impact) {
                    renderImpact(res.data.impact);
                }

                if (res.data.developmentPipeline) {
                    renderDevelopmentPipeline(res.data.developmentPipeline);
                }

                if (res.data.alerts) {
                    renderAlerts(res.data.alerts);
                }


            } else {
                console.error("Invalid API response", res);
                alert("Failed to load dashboard data");
            }
        },
        error: function (xhr, status, error) {
            console.error("API Error:", error);
            alert("Something went wrong while loading dashboard");
        }
    });
}

// HEADER SECTION
function renderHeader() {

    const now = new Date();

    const monthYear = now.toLocaleString('default', {
        month: 'long',
        year: 'numeric'
    });

    const time = now.toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit'
    });

    $("#headerMonth").text(monthYear);
    $("#headerLastUpdated").text(`Last Updated: Today at ${time}`);
}
function renderSystemHealth(d) {

    $("#activeVolunteers").text(getValue(d.activeVolunteers));
    $("#activeTeamLeads").text(getValue(d.activeTeamLeads));
    $("#visitors").text(getValue(d.visitorsMTD));
    $("#completed").text(getValue(d.followUpsCompletedMTD));

    $("#systemVNPS").text(formatVNPS(d.systemVNPS, d.vnpsStatus));
    $("#retention").text(formatPercent(d.volunteerRetention));
    $("#completion").text(formatPercent(d.completionRateMTD));
    $("#avgResponse").text(formatDays(d.avgResponseTime));

    // Overall Health
    $("#overallHealthText").text(getValue(d.overallFlag, "N/A"));

    setHealthDot(d.overallFlag);
}
function getValue(val, fallback = 0) {
    return (val === null || val === undefined || val === "") ? fallback : val;
}

function formatPercent(val) {
    if (val === null || val === undefined) return "0%";
    return `${val}%`;
}

function formatDays(val) {
    if (val === null || val === undefined) return "0 days";
    return `${val} days`;
}

function formatVNPS(val, status) {
    if (val === null || val === undefined) return "0";

    if (!status) return `${val}`;
    return `${val} (${status})`;
}

// DOT COLOR LOGIC
function setHealthDot(status) {
    let color = "#00ff88"; // default green

    if (status === "Warning") color = "#ffcc00";
    if (status === "Critical") color = "#ff4444";

    $("#healthDot").css("background", color);
}


// KPI SECTION
function renderKPIs(k) {

    let rows = "";

    rows += buildRow("Completion Rate", k.completionRate);
    rows += buildRow("First Contact <48h", k.firstContact48h);
    rows += buildRow("Escalation Rate", k.escalationRate);
    rows += buildRow("Crisis Handled Safely", k.crisisHandledSafely);
    rows += buildRow("Volunteer Retention", k.volunteerRetention);
    rows += buildRow("System vNPS", k.systemVNPS, false); // not %

    $("#kpiRows").html(rows);

    $("#kpiOverall").html(
        `Overall: ${getValue(k.onTrack, 0)}/6 metrics on target <span class="check">✔</span>`
    );
}
function buildRow(title, item, isPercent = true) {

    if (!item) item = {};

    const current = formatValue(item.current, isPercent);
    const target = item.target ?? "-";

    const statusDot = getStatusDot(item.status);
    const trend = getTrend(item.trend);

    return `
        <div class="kpi-row">
            <div>${title}</div>
            <div>${current}</div>
            <div>${target}</div>
            <div>${statusDot}</div>
            <div>${trend}</div>
        </div>
    `;
}
function formatValue(val, isPercent) {
    if (val === null || val === undefined) return "0";
    return isPercent ? `${val}%` : val;
}

function getStatusDot(status) {
    let color = "#00ff88"; // green

    if (status === "Warning") color = "#ffcc00";
    if (status === "Critical") color = "#ff4444";

    return `<span class="dot" style="background:${color}"></span>`;
}

function getTrend(val) {

    if (val === null || val === undefined) return "→";

    if (val > 0) return `⬆ +${val}${Math.abs(val) <= 100 ? "%" : ""}`;
    if (val < 0) return `⬇ ${val}${Math.abs(val) <= 100 ? "%" : ""}`;

    return `→`;
}



// TEAM LEAD PERFORMANCE
function renderTeamLeads(data) {

    let rows = "";

    data.forEach(tl => {
        rows += buildTLRow(tl);
    });

    $("#teamLeadRows").html(rows);

    renderAttention(data);
}
function buildTLRow(tl) {

    return `
        <div class="tl-row">
            <div>${getValue(tl.teamLeadName, "-")}</div>
            <div>${getValue(tl.teamSize)}</div>
            <div>${formatPercent(tl.completionRate)}</div>
            <div>${getValue(tl.teamVNPS)}</div>
            <div>${formatPercent(tl.retentionRate)}</div>
            <div class="flag">${getValue(tl.flag, "-")}</div>
        </div>
    `;
}

function getFlagDot(flag) {

    let color = "#00ff88"; // green

    if (flag === "Warning") color = "#ffcc00";
    if (flag === "Critical") color = "#ff4444";

    return `<span class="dot" style="background:${color}"></span>`;
}

function renderAttention(data) {

    let html = "";

    data.forEach(tl => {

        if (tl.belowTargetCount >= 2) {
            html += `<div>- ${tl.teamLeadName} - Below target on ${tl.belowTargetCount} metrics (needs support)</div>`;
        }
        else if (tl.belowTargetCount === 1) {
            html += `<div>- ${tl.teamLeadName} - Slightly below target (monitor)</div>`;
        }

    });

    if (html) {
        $("#attentionSection").html(`
            <div class="attention-title">⚠ Attention Needed:</div>
            ${html}
        `);
    } else {
        $("#attentionSection").html("");
    }
}

// PIPELINE HEALTH
function renderPipeline(data) {

    if (!data || !data.stages) return;

    let rows = "";

    data.stages.forEach(s => {
        rows += buildPipelineRow(s);
    });

    $("#pipelineRows").html(rows);

    renderPipelineSummary(data);
}
function buildPipelineRow(s) {

    return `
        <div class="pl-row">
            <div>${formatStageName(s.followUpStatus)}</div>
            <div>${getValue(s.count)}</div>
            <div>${formatPercent(s.percentage)}</div>
            <div>${formatDaysOrDash(s.avgDaysInStage)}</div>
        </div>
    `;
}
function renderPipelineSummary(data) {

    let html = "";

    html += `
        <div class="health-line">
            <span class="dot" style="background:#00ff88"></span>
            Healthy Pipeline: ${getValue(data.successRate)}% successful contact rate
        </div>
    `;

    $("#pipelineSummary").html(html);
}
function formatDaysOrDash(val) {
    if (val === null || val === undefined) return "-";
    return `${val} days`;
}
function formatStageName(status) {

    if (!status) return "-";

    return status
        .toLowerCase()
        .replace(/_/g, " ")
        .replace(/\b\w/g, c => c.toUpperCase());
}


// ================= ESCALATIONS SECTION =================
function renderEscalations(data) {

    if (!data || !data.summary) {
        $("#escalationContent").html("No escalation data");
        return;
    }

    const s = data.summary;

    let html = "";

    // ================= THIS MONTH =================
    html += `
        <div class="escalation-block">
            <div class="escalation-title">This Month:</div>
            <div class="escalation-item">- Total Escalations: ${getValue(s.totalEscalations)}</div>
            <div class="escalation-item">- Standard (Needs Follow-Up): ${getValue(s.standardCount)}</div>
            <div class="escalation-item">- Urgent: ${getValue(s.urgentCount)}</div>
            <div class="escalation-item">- Emergency (Crisis): ${getValue(s.emergencyCount)}</div>
        </div>
    `;

    // ================= RESOLUTION TIME (NOW DYNAMIC) =================
    html += `
        <div class="escalation-block">
            <div class="escalation-title">Resolution Time:</div>
            <div class="escalation-item">
                - Standard: Avg ${formatDaysSafe(s.avgResolutionStandard)} (target <2 days) ${getCheck(s.avgResolutionStandard, 2)}
            </div>
            <div class="escalation-item">
                - Urgent: Avg ${formatDaysSafe(s.avgResolutionUrgent)} (target <1 day) ${getCheck(s.avgResolutionUrgent, 1)}
            </div>
            <div class="escalation-item">
                - Emergency: Immediate handling expected ${getEmergencyCheck(s.pendingEmergency)}
            </div>
        </div>
    `;

    // ================= PENDING =================
    html += `
        <div class="escalation-block">
            <div class="escalation-title">Pending Escalations:</div>
            <div class="escalation-item">- Standard: ${getValue(s.pendingStandard)}</div>
            <div class="escalation-item">- Urgent: ${getValue(s.pendingUrgent)}</div>
            <div class="escalation-item">- Emergency: ${getValue(s.pendingEmergency)}</div>
        </div>
    `;

    // ================= TOP REASONS =================
    if (data.topReasons && data.topReasons.length > 0) {

        html += `<div class="escalation-block">
                    <div class="escalation-title">Top Escalation Reasons:</div>`;

        data.topReasons.forEach((r, i) => {
            html += `<div class="escalation-item">
                        ${i + 1}. ${r.reason} (${r.count} cases)
                     </div>`;
        });

        html += `</div>`;
    }

    // ================= INSIGHT =================
    html += `
        <div class="insight">
            💡 Insight: Monitor escalation patterns and provide support where needed
        </div>
    `;

    $("#escalationContent").html(html);
}
function formatDaysSafe(val) {
    if (val === null || val === undefined) return "-";
    return `${val.toFixed(1)} days`;
}

function getCheck(actual, target) {
    if (actual === null || actual === undefined) return "";
    return actual <= target ? `<span class="check">✔</span>` : `⚠`;
}

function getEmergencyCheck(pending) {
    return pending === 0 ? `<span class="check">✔</span>` : `⚠`;
}

// ================= TREND SECTION =================
function renderTrends(data) {

    if (!data || data.length === 0) {
        $("#trendRows").html("No trend data");
        return;
    }

    renderTrendHeader(data);
    renderTrendRows(data);
    renderTrendInsights(data);
}
function renderTrendHeader(data) {

    let header = `<div>Metric</div>`;

    data.forEach(m => {
        header += `<div>${m.monthName}</div>`;
    });

    header += `<div>Trend</div>`;

    $("#trendHeader").html(header);
}
function renderTrendRows(data) {

    let html = "";

    html += buildTrendRow("First-Time Visitors", data.map(x => x.visitors));
    html += buildTrendRow("Completion Rate", data.map(x => x.completionRate), true);
    html += buildTrendRow("System vNPS", data.map(x => x.vnps));
    html += buildTrendRow("Volunteer Count", data.map(x => x.volunteerCount));
    html += buildTrendRow("Crisis Cases", data.map(x => x.crisisCount));
    html += buildTrendRow("Volunteer Turnover", data.map(x => x.turnoverCount));

    $("#trendRows").html(html);
}
function buildTrendRow(title, values, isPercent = false) {

    let row = `<div class="trend-row">`;

    row += `<div>${title}</div>`;

    values.forEach(v => {
        row += `<div>${formatTrendValue(v, isPercent)}</div>`;
    });

    row += `<div>${calculateTrend(values)}</div>`;

    row += `</div>`;

    return row;
}
function formatTrendValue(val, isPercent) {
    if (val === null || val === undefined) return "-";
    return isPercent ? `${Math.round(val)}%` : val;
}
function calculateTrend(values) {

    if (!values || values.length < 2) return "→";

    const first = values[0] || 0;
    const last = values[values.length - 1] || 0;

    if (last > first) return "⬆ Improving";
    if (last < first) return "⬇ Declining";
    return "→ Stable";
}
function renderTrendInsights(data) {

    let html = "";

    const last = data[data.length - 1];

    // Positive insights
    html += `<div>🎉 Positive Trends:</div>`;

    if (last.visitors > 0) {
        html += `<div>- Visitor activity present</div>`;
    }

    if (last.turnoverCount === 0) {
        html += `<div>- Zero volunteer turnover</div>`;
    }

    if (last.vnps !== null) {
        html += `<div>- vNPS data available</div>`;
    }

    // Warning
    html += `<div class="trend-warning">⚠ Watch:</div>`;

    if (last.visitors > 0) {
        html += `<div>- Monitor growth for scaling needs</div>`;
    } else {
        html += `<div>- No visitor activity recorded</div>`;
    }

    $("#trendInsights").html(html);
}


// ================= IMPACT SECTION =================
function renderImpact(data) {

    if (!data) {
        $("#impactContent").html("No impact data");
        return;
    }

    let html = "";

    // ================= THIS MONTH =================
    html += `
        <div class="impact-block">
            <div class="impact-title">This Month:</div>

            <div class="impact-item">✔ ${getValue(data.totalConversations)} follow-up conversations completed</div>
            <div class="impact-item">✔ ${getValue(data.smallGroupConnections)} people connected to small groups</div>
            <div class="impact-item">✔ ${getValue(data.prayerCount)} people received prayer</div>
            <div class="impact-item">✔ ${getValue(data.benevolenceCount)} people connected to benevolence</div>
            <div class="impact-item">✔ ${getValue(data.counselingCount)} people scheduled counseling</div>
            <div class="impact-item">✔ ${getValue(data.serveConnections)} people connected to serve teams</div>
        </div>
    `;

    // ================= QUARTER (DERIVED - OPTIONAL) =================
    html += `
        <div class="impact-block">
            <div class="impact-title">Quarter-to-Date:</div>

            <div class="impact-item">✔ ${getValue(data.totalConversations)} total contacts</div>
            <div class="impact-item">✔ Engagement activity recorded</div>
            <div class="impact-item">✔ Community connections in progress</div>
        </div>
    `;

    $("#impactContent").html(html);
}


// ================= DEVELOPMENT PIPELINE =================
function renderDevelopmentPipeline(data) {

    if (!data) {
        $("#dpRows").html("No pipeline data");
        return;
    }

    let html = "";

    html += buildDPRow("Level 0 (In Training)", data.level0, "New volunteers onboarding");
    html += buildDPRow("Level 1 (Active)", data.level1Active, "Currently serving");
    html += buildDPRow("Level 1 (Care Path)", data.level1CarePath, "Support / recovery");
    html += buildDPRow("Ready for Level 2 Promotion", data.promotionReady, "Eligible for promotion");
    html += buildDPRow("Level 2 (Prayer Ministry)", data.level2, "Advanced roles");

    $("#dpRows").html(html);

    renderDPSummary(data);
}
function buildDPRow(stage, count, note) {

    return `
        <div class="dp-row">
            <div>${stage}</div>
            <div>${getValue(count)}</div>
            <div>${note}</div>
        </div>
    `;
}
function renderDPSummary(data) {

    let summary = "";

    if (data.promotionReady > 0) {
        summary = `<span class="dot"></span> Strong bench of promotion-ready volunteers`;
    } else if (data.level1Active > 0) {
        summary = `<span class="dot" style="background:#ffcc00"></span> Stable pipeline`;
    } else {
        summary = `<span class="dot" style="background:#ff4444"></span> Pipeline needs attention`;
    }

    $("#dpSummary").html(`Pipeline Health: ${summary}`);
}

// ================= ALERTS SECTION =================
function renderAlerts(data) {

    if (!data) {
        $("#alertsContent").html("No alerts");
        return;
    }

    let html = "";

    html += buildAlertBlock("URGENT (This Week)", data.urgent);
    html += buildAlertBlock("IMPORTANT (This Month)", data.important);
    html += buildAlertBlock("STRATEGIC (Next Quarter)", data.strategic);

    $("#alertsContent").html(html);
}
function buildAlertBlock(title, items) {

    let html = `<div class="alert-block">
                    <div class="alert-title">${title}:</div>`;

    if (!items || items.length === 0) {
        html += `<div class="alert-item">- No alerts</div>`;
    } else {
        items.forEach(a => {
            html += `
                <div class="alert-item">
                    - ${getValue(a.message)}
                </div>
                ${a.action ? `<div class="alert-action">→ Action: ${a.action}</div>` : ""}
            `;
        });
    }

    html += `</div>`;

    return html;
}