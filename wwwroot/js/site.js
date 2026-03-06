// Client-side adapter for [NotFutureDate] server attribute
// Registered before DOMContentLoaded so unobtrusive validation picks it up
if (window.jQuery && jQuery.validator && jQuery.validator.unobtrusive) {
    jQuery.validator.addMethod('notfuturedate', function (value, element) {
        if (!value) return true; // let [Required] handle empty
        var today = new Date();
        today.setHours(0, 0, 0, 0);
        var input = new Date(value);
        return input <= today;
    });
    jQuery.validator.unobtrusive.adapters.addBool('notfuturedate');
}

document.addEventListener('DOMContentLoaded', function () {
    // Page scroll detection: list pages keep overflow:hidden on .page (table scrolls internally).
    // Non-list pages (forms, details, profiles, settings) need page-level scrolling.
    // Dashboards use .dash-page which handles its own overflow.
    var pageEl = document.querySelector('main.page');
    if (pageEl) {
        // Explicit override: data-page-scroll="true" on any element forces page scroll
        var explicitScroll = pageEl.querySelector('[data-page-scroll]');
        // Dashboards handle their own scrolling via .dash-page
        var isDashboard = pageEl.querySelector('.dash-page');
        // List pages use .md-card or .rc for table containers
        var isListPage = pageEl.querySelector('.md-card, .rc');

        if (explicitScroll || (!isDashboard && !isListPage)) {
            pageEl.style.overflowY = 'auto';
        }
    }

    // Confirmation dialogs for destructive actions
    document.querySelectorAll('[data-confirm]').forEach(function (el) {
        el.addEventListener('click', function (e) {
            if (!confirm(this.getAttribute('data-confirm'))) {
                e.preventDefault();
                e.stopPropagation();
            }
        });
    });

    // Auto-dismiss alerts after 6 seconds
    document.querySelectorAll('.alert-dismissible').forEach(function (alert) {
        setTimeout(function () {
            alert.style.transition = 'opacity 0.3s ease-out';
            alert.style.opacity = '0';
            setTimeout(function () { alert.remove(); }, 300);
        }, 6000);
    });

    // Close alert button
    document.querySelectorAll('.btn-close').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var alert = this.closest('.alert');
            if (alert) {
                alert.style.transition = 'opacity 0.2s ease-out';
                alert.style.opacity = '0';
                setTimeout(function () { alert.remove(); }, 200);
            }
        });
    });


    // Announcement popup system — only runs when user is authenticated (navbar present).
    // Login page and first-login password change page have no nav, so this safely skips them.
    (function () {
        if (!document.querySelector('nav')) return; // unauthenticated page — skip announcements

        var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

        // Fetch unread count for badge
        fetch('/Announcement/UnreadCount')
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (data) {
                if (data.count > 0) {
                    var badges = document.querySelectorAll('#unreadBadge, #navUnreadBadge');
                    badges.forEach(function (b) {
                        b.textContent = data.count;
                        b.style.display = '';
                    });
                }
            }).catch(function () { });

        // Fetch popup announcements
        fetch('/Announcement/GetPopupAnnouncements')
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (announcements) {
                if (!announcements || announcements.length === 0) return;

                var modal = document.getElementById('announcementModal');
                var body = document.getElementById('announcementModalBody');
                var counter = document.getElementById('announcementCounter');
                var prevBtn = document.getElementById('prevAnnouncement');
                var nextBtn = document.getElementById('nextAnnouncement');
                var markReadBtn = document.getElementById('markReadAnnouncement');
                var closeBtn = document.getElementById('closeAnnouncementModal');
                var backdrop = document.getElementById('modalBackdrop');
                if (!modal) return;

                var current = 0;

                function showAnnouncement(idx) {
                    var a = announcements[idx];
                    body.innerHTML = '';
                    var h4 = document.createElement('h4');
                    h4.textContent = a.title;
                    var p = document.createElement('p');
                    p.style.whiteSpace = 'pre-line';
                    p.textContent = a.message;
                    var footer = document.createElement('div');
                    footer.className = 'small text-muted mt-2';
                    footer.textContent = 'From: ' + a.createdBy + ' \u2014 ' + a.createdAt;
                    body.appendChild(h4);
                    body.appendChild(p);
                    body.appendChild(footer);

                    // Update counter
                    if (announcements.length > 1) {
                        counter.textContent = (idx + 1) + ' of ' + announcements.length;
                        counter.style.display = '';
                    } else {
                        counter.style.display = 'none';
                    }

                    // Show/hide nav buttons — only when multiple announcements
                    prevBtn.style.display = (announcements.length > 1 && idx > 0) ? '' : 'none';
                    nextBtn.style.display = (announcements.length > 1 && idx < announcements.length - 1) ? '' : 'none';
                }

                function markCurrentAsRead() {
                    var a = announcements[current];
                    if (!a || !token) return;

                    // Disable button while processing
                    markReadBtn.disabled = true;
                    markReadBtn.innerHTML = '<i class="bi bi-hourglass-split" style="margin-right:4px;"></i>Marking...';

                    fetch('/Announcement/DismissAjax/' + a.id, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token }
                    }).then(function (r) { return r.json(); })
                        .then(function () {
                            // Remove current announcement from list
                            announcements.splice(current, 1);

                            // Update badge count
                            var badges = document.querySelectorAll('#unreadBadge, #navUnreadBadge');
                            badges.forEach(function (b) {
                                var c = parseInt(b.textContent || '0') - 1;
                                if (c <= 0) { b.style.display = 'none'; }
                                else { b.textContent = c; }
                            });

                            // If no more announcements, close modal
                            if (announcements.length === 0) {
                                modal.style.display = 'none';
                                return;
                            }

                            // Adjust index if needed
                            if (current >= announcements.length) current = announcements.length - 1;

                            // Reset button and show next
                            markReadBtn.disabled = false;
                            markReadBtn.innerHTML = '<i class="bi bi-check-circle" style="margin-right:4px;"></i>Mark as Read';
                            showAnnouncement(current);
                        }).catch(function () {
                            markReadBtn.disabled = false;
                            markReadBtn.innerHTML = '<i class="bi bi-check-circle" style="margin-right:4px;"></i>Mark as Read';
                        });
                }

                function closeModal() {
                    modal.style.display = 'none';
                    // Mark remaining as "popup shown" (bumps counter, doesn't dismiss)
                    var ids = announcements.map(function (a) { return a.id; });
                    if (ids.length > 0 && token) {
                        fetch('/Announcement/MarkPopupShown', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                            body: JSON.stringify({ announcementIds: ids })
                        }).catch(function () { });
                    }
                }

                prevBtn.addEventListener('click', function () {
                    if (current > 0) { current--; showAnnouncement(current); }
                });
                nextBtn.addEventListener('click', function () {
                    if (current < announcements.length - 1) { current++; showAnnouncement(current); }
                });
                markReadBtn.addEventListener('click', markCurrentAsRead);
                closeBtn.addEventListener('click', closeModal);
                backdrop.addEventListener('click', closeModal);

                showAnnouncement(0);
                modal.style.display = '';
            }).catch(function () { });
    })();

    // AJAX inline creation for master data dropdowns
    document.querySelectorAll('[data-inline-create]').forEach(function (btn) {
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            var selectId = this.getAttribute('data-target-select');
            var endpoint = this.getAttribute('data-endpoint');
            var selectEl = document.getElementById(selectId);
            var label = this.getAttribute('data-label') || 'item';
            var newValue = prompt('Enter new ' + label + ' name:');
            if (!newValue || newValue.trim() === '') return;

            var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            fetch(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({ Name: newValue.trim() })
            })
                .then(function (res) {
                    if (!res.ok) {
                        if (res.status === 403) {
                            throw new Error('You do not have permission to create new items.');
                        }
                        throw new Error('Server error (' + res.status + '). Please try again.');
                    }
                    return res.json();
                })
                .then(function (data) {
                    if (data.success) {
                        var option = document.createElement('option');
                        option.value = data.id;
                        option.text = data.name;
                        option.selected = true;
                        selectEl.appendChild(option);
                    } else {
                        alert(data.message || 'Error creating item');
                    }
                })
                .catch(function (err) {
                    console.error(err);
                    showToast(err.message || 'Error creating item. Please try again.', 'error');
                });
        });
    });

    // P3 FIX: Toast notification system for AJAX errors
    function showToast(message, type) {
        var toast = document.createElement('div');
        toast.className = 'toast-notification toast-' + (type || 'info');
        var icon = document.createElement('i');
        icon.className = 'bi bi-' + (type === 'error' ? 'exclamation-circle-fill' : 'info-circle-fill');
        var span = document.createElement('span');
        span.textContent = message;
        toast.appendChild(icon);
        toast.appendChild(span);
        document.body.appendChild(toast);
        setTimeout(function () { toast.classList.add('show'); }, 10);
        setTimeout(function () {
            toast.classList.remove('show');
            setTimeout(function () { toast.remove(); }, 300);
        }, 5000);
    }

    // Make showToast available globally
    window.showToast = showToast;
});
