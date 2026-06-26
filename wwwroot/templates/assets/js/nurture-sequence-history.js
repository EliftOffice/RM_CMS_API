/**
 * Nurture Sequence History Module
 * Handles the display and interaction with nurture sequence audit views
 * 
 * Usage:
 *   const historyModule = new NurtureSequenceHistory({
 *       apiBaseUrl: '/api/nurture',
 *       containerId: 'history-container'
 *   });
 *   historyModule.loadSequence('NS0001');
 */

class NurtureSequenceHistory {
    constructor(options = {}) {
        this.apiBaseUrl = options.apiBaseUrl || '/api/nurture';
        this.containerId = options.containerId || 'nurture-history-container';
        this.container = document.getElementById(this.containerId);
        this.stepsData = null;

        if (!this.container) {
            console.error(`Container with ID '${this.containerId}' not found`);
        }
    }

    /**
     * Load sequence history from API
     */
    async loadSequence(sequenceId) {
        try {
            this.showLoading();

            const response = await fetch(`${this.apiBaseUrl}/sequence/${sequenceId}/steps`);

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const result = await response.json();

            if (result.responseType !== 'Success') {
                throw new Error(result.message || 'Failed to load sequence history');
            }

            this.stepsData = result.data;
            this.render(this.stepsData);

        } catch (error) {
            console.error('Error loading sequence history:', error);
            this.showError(error.message);
        }
    }

    /**
     * Render the complete audit view
     */
    render(steps) {
        if (!this.container) return;

        if (!steps || steps.length === 0) {
            this.container.innerHTML = this.getEmptyStateHtml();
            return;
        }

        const completedCount = steps.filter(s => s.status === 'Done').length;
        const progressPercent = (completedCount / steps.length) * 100;
        const firstStep = steps[0];

        const html = `
            <div class="nurture-audit-view">
                ${this.getHeaderHtml(firstStep, completedCount, steps.length)}
                <div class="nurture-timeline">
                    ${steps.map((step, idx) => this.getStepItemHtml(step, idx)).join('')}
                </div>
                ${this.getFooterHtml(steps)}
            </div>
        `;

        this.container.innerHTML = html;
        this.attachEventListeners();
    }

    /**
     * Get header HTML
     */
    getHeaderHtml(firstStep, completedCount, totalCount) {
        const progressPercent = (completedCount / totalCount) * 100;
        const status = completedCount === totalCount ? 'Completed' :
            completedCount > 0 ? 'In Progress' : 'Active';
        const statusClass = status.toLowerCase().replace(/\s+/g, '-');

        return `
            <div class="nurture-header" style="background: linear-gradient(135deg, #085c40 0%, #0d7a52 100%); color: white; padding: 30px; border-radius: 12px 12px 0 0;">
                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 30px;">
                    <div>
                        <h2 style="font-size: 24px; margin: 0 0 20px 0; display: flex; align-items: center; gap: 10px;">
                            <i class="fas fa-seedling"></i>Nurture Sequence History
                        </h2>
                        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 15px; font-size: 14px;">
                            <div>
                                <div style="opacity: 0.8; text-transform: uppercase; font-size: 11px; margin-bottom: 4px;">Person</div>
                                <div style="font-weight: 600; font-size: 16px;">${firstStep.personName || '—'}</div>
                            </div>
                            <div>
                                <div style="opacity: 0.8; text-transform: uppercase; font-size: 11px; margin-bottom: 4px;">Volunteer</div>
                                <div style="font-weight: 600; font-size: 16px;">${firstStep.volunteerName || '—'}</div>
                            </div>
                            <div>
                                <div style="opacity: 0.8; text-transform: uppercase; font-size: 11px; margin-bottom: 4px;">Sequence ID</div>
                                <div style="font-weight: 600; font-size: 16px; font-family: monospace;">${firstStep.sequenceId || '—'}</div>
                            </div>
                            <div>
                                <div style="opacity: 0.8; text-transform: uppercase; font-size: 11px; margin-bottom: 4px;">Started</div>
                                <div style="font-weight: 600; font-size: 16px;">${this.formatDate(firstStep.scheduledDate)}</div>
                            </div>
                        </div>
                        <div style="margin-top: 20px; padding-top: 20px; border-top: 1px solid rgba(255, 255, 255, 0.2); display: flex; gap: 10px;">
                            <span class="nurture-status-badge nurture-status-${statusClass}" style="padding: 8px 16px; border-radius: 20px; font-size: 12px; font-weight: 600; text-transform: uppercase;">
                                ${status}
                            </span>
                        </div>
                    </div>
                    <div style="text-align: right;">
                        <div style="margin-bottom: 12px;">
                            <div style="font-size: 14px; margin-bottom: 8px;">
                                <strong>${completedCount}</strong> of <strong>${totalCount}</strong> Steps Completed
                            </div>
                            <div style="width: 100%; height: 8px; background: rgba(255, 255, 255, 0.3); border-radius: 10px; overflow: hidden;">
                                <div style="height: 100%; width: ${progressPercent}%; background: #10b981;"></div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Get step item HTML
     */
    getStepItemHtml(step, index) {
        const statusClass = `nurture-status-${step.status.toLowerCase()}`;
        const statusText = step.status === 'Done' ? '✓ Done' :
            step.status === 'Missed' ? '✗ Missed' :
                '⊘ Pending';

        const responseClass = step.responseType ?
            `nurture-response-${step.responseType.toLowerCase().replace(/ /g, '-')}` : '';

        const contactStatusIcon = step.contactStatus === 'Contacted' ?
            'fa-check-circle' : 'fa-times-circle';
        const contactStatusClass = step.contactStatus === 'Contacted' ?
            'nurture-contact-contacted' : 'nurture-contact-not-contacted';

        const delayMs = (index * 100);
        const animationDelay = `${delayMs}ms`;

        return `
            <div class="nurture-timeline-item nurture-${step.status.toLowerCase()}" style="animation-delay: ${animationDelay};">
                <div class="nurture-step-card">
                    <div class="nurture-step-header">
                        <div class="nurture-step-number">${step.stepNumber}</div>
                        <div class="nurture-step-title">
                            <h3 style="margin: 0; font-size: 16px; font-weight: 600;">${this.getStepTitle(step.stepNumber)}</h3>
                            <span class="nurture-method-badge">${step.method}</span>
                        </div>
                        <div class="nurture-step-dates">
                            <i class="fas fa-calendar-alt"></i> 
                            ${this.formatDate(step.scheduledDate)}
                        </div>
                        <span class="nurture-status-pill ${statusClass}">${statusText}</span>
                    </div>
                    
                    <div class="nurture-step-details">
                        <div class="nurture-detail-field">
                            <div class="nurture-detail-label">Scheduled Date</div>
                            <div class="nurture-detail-value">${this.formatDate(step.scheduledDate)}</div>
                        </div>
                        
                        <div class="nurture-detail-field">
                            <div class="nurture-detail-label">Completed Date</div>
                            <div class="nurture-detail-value ${step.completedAt ? '' : 'nurture-empty'}">
                                ${step.completedAt ? this.formatDate(step.completedAt) : 'Not completed'}
                            </div>
                        </div>
                        
                        <div class="nurture-detail-field">
                            <div class="nurture-detail-label">Method</div>
                            <div class="nurture-detail-value">${step.method}</div>
                        </div>
                        
                        ${step.contactStatus ? `
                            <div class="nurture-detail-field">
                                <div class="nurture-detail-label">Contact Status</div>
                                <div class="nurture-detail-value nurture-contact-status ${contactStatusClass}">
                                    <i class="fas ${contactStatusIcon}"></i>
                                    ${step.contactStatus}
                                </div>
                            </div>
                        ` : ''}
                        
                        ${step.responseType ? `
                            <div class="nurture-detail-field">
                                <div class="nurture-detail-label">Response Type</div>
                                <div class="nurture-detail-value">
                                    <span class="nurture-response-badge ${responseClass}">
                                        ${step.responseType}
                                    </span>
                                </div>
                            </div>
                        ` : ''}
                        
                        <div class="nurture-detail-field">
                            <div class="nurture-detail-label">Status</div>
                            <div class="nurture-detail-value">
                                <span class="nurture-status-pill ${statusClass}">${step.status}</span>
                            </div>
                        </div>
                    </div>
                    
                    ${step.notes ? `
                        <div class="nurture-notes-section ${step.responseType === 'Crisis' ? 'nurture-crisis' : ''}">
                            <div class="nurture-notes-label">
                                <i class="fas fa-sticky-note"></i>Notes
                            </div>
                            <div class="nurture-notes-text">${this.escapeHtml(step.notes)}</div>
                        </div>
                    ` : ''}
                </div>
            </div>
        `;
    }

    /**
     * Get footer HTML with action buttons
     */
    getFooterHtml(steps) {
        return `
            <div class="nurture-footer" style="background: #f9fafb; padding: 20px 30px; border-top: 1px solid #e5e7eb; display: flex; justify-content: space-between; align-items: center; border-radius: 0 0 12px 12px;">
                <div style="font-size: 13px; color: #6b7280;">
                    <span id="nurture-last-updated">Last updated: ${new Date().toLocaleTimeString()}</span>
                </div>
                <div style="display: flex; gap: 15px;">
                    <button class="nurture-btn nurture-btn-secondary" onclick="this.closest('.nurture-audit-view').NurtureModule?.exportToCSV()">
                        <i class="fas fa-download"></i>Export CSV
                    </button>
                    <button class="nurture-btn nurture-btn-primary" onclick="window.print()">
                        <i class="fas fa-print"></i>Print/PDF
                    </button>
                </div>
            </div>
        `;
    }

    /**
     * Get empty state HTML
     */
    getEmptyStateHtml() {
        return `
            <div class="nurture-empty-state" style="text-align: center; padding: 60px 40px; color: #9ca3af;">
                <i class="fas fa-inbox" style="font-size: 48px; margin-bottom: 16px; color: #d1d5db;"></i>
                <h3 style="font-size: 18px; margin-bottom: 8px; color: #6b7280;">No Steps Found</h3>
                <p>No nurture steps have been recorded for this sequence yet.</p>
            </div>
        `;
    }

    /**
     * Get step title
     */
    getStepTitle(stepNumber) {
        const titles = [
            'First Contact Call',
            'First In-Person Visit',
            'Second Contact Call',
            'Second In-Person Visit',
            'Third Contact Call',
            'Third In-Person Visit',
            'Final Contact Call & Decision'
        ];
        return titles[stepNumber - 1] || `Step ${stepNumber}`;
    }

    /**
     * Format date
     */
    formatDate(dateString) {
        if (!dateString) return '—';
        const date = new Date(dateString);
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    }

    /**
     * Escape HTML
     */
    escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /**
     * Show loading state
     */
    showLoading() {
        if (this.container) {
            this.container.innerHTML = `
                <div style="text-align: center; padding: 40px; color: #6b7280;">
                    <i class="fas fa-spinner" style="font-size: 32px; margin-bottom: 12px; animation: spin 1s linear infinite;"></i>
                    <div>Loading sequence history...</div>
                </div>
                <style>
                    @keyframes spin {
                        0% { transform: rotate(0deg); }
                        100% { transform: rotate(360deg); }
                    }
                </style>
            `;
        }
    }

    /**
     * Show error state
     */
    showError(message) {
        if (this.container) {
            this.container.innerHTML = `
                <div style="background: #fee2e2; border: 1px solid #fecaca; color: #991b1b; padding: 16px; border-radius: 8px;">
                    <i class="fas fa-exclamation-circle"></i> ${message}
                </div>
            `;
        }
    }

    /**
     * Export to CSV
     */
    exportToCSV() {
        if (!this.stepsData || this.stepsData.length === 0) {
            console.warn('No data to export');
            return;
        }

        const headers = [
            'Step #',
            'Title',
            'Method',
            'Scheduled Date',
            'Completed Date',
            'Status',
            'Contact Status',
            'Response Type',
            'Notes'
        ];

        const rows = this.stepsData.map((step) => [
            step.stepNumber,
            this.getStepTitle(step.stepNumber),
            step.method,
            this.formatDate(step.scheduledDate),
            step.completedAt ? this.formatDate(step.completedAt) : 'Not completed',
            step.status,
            step.contactStatus || '—',
            step.responseType || '—',
            step.notes ? `"${step.notes.replace(/"/g, '""')}"` : ''
        ]);

        const csvContent = [
            headers.join(','),
            ...rows.map(row => row.join(','))
        ].join('\n');

        const blob = new Blob([csvContent], { type: 'text/csv' });
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;

        const sequenceId = this.stepsData[0]?.sequenceId || 'unknown';
        link.download = `nurture-sequence-${sequenceId}-${Date.now()}.csv`;
        link.click();
        window.URL.revokeObjectURL(url);
    }

    /**
     * Attach event listeners
     */
    attachEventListeners() {
        if (this.container) {
            const auditView = this.container.querySelector('.nurture-audit-view');
            if (auditView) {
                auditView.NurtureModule = this;
            }
        }
    }
}

// Export for use as module
if (typeof module !== 'undefined' && module.exports) {
    module.exports = NurtureSequenceHistory;
}
